// Project: sboxgamejam3
// File:    Hud.razor.cs
// Author:  cseliot
// Created: 2026.09.19
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// Pub-crawling HUD (Design.md, Screen 1): binding + display formatting for
// Hud.razor. Reads the local player's PlayerProgress (Timer/Score) and DrunkCC
// (BeerLevel) through a [Property] GameManager reference - never mutates them
// (PlayerProgress is owner-authoritative; this is a read-only view). All state
// lives in those components, so this partial holds no game state of its own.
//
// Also feeds the TOP-LEFT PLAYER LIST COLUMN (design change 09-24: the list replaced
// the standalone center SCORE readout, now deleted): UpdatePlayerListHelper enumerates
// every tagged "player" body each frame and rebuilds a sorted row list (name + floored
// live [Sync] Score, local row pinned first) that Hud.razor renders. Read-only over
// PlayerProgress/Connection - it never mutates them. The render tree repaints throttled
// (_TextRefreshRate) so digits tick calmly; OnUpdate itself stays every-frame.
//
// Also gates the AUTOMATIC PANTS-DOWN WARNING: the right-edge readout is hidden by
// default and appears (blinking) once the local DrunkCC has been continuously
// pants-DOWN for _PantsDownWarnSeconds; it resets the moment the pants come back up.
//
// Also owns the TARGET-BAR PREVIEW: a ScenePanel (<scene> element in the markup,
// gated on Visible) whose private RenderScene hosts a clone of the current
// target bar's -JustModel prefab (paired by BarModule display name via the
// _BarPreviewNames/_BarPreviewPrefabs arrays wired in the scene), spinning on a
// slightly-kiltered turntable, camera auto-framed from the model's renderer
// bounds. ScenePanel composites into the UI layer, so the preview always draws
// on top of in-world models. Any leftover gameplay components (colliders,
// NavMeshArea, particles) are stripped from the clone - it is a pure visual.
//
// Naming note: this file is Hud.razor.cs (longer path than Hud.razor), so the
// engine's shortest-source-path stylesheet derivation still resolves to
// Hud.razor.scss. Do NOT add a bare Hud.cs to this partial - it would become the
// shortest path and make the panel look for a nonexistent Hud.cs.scss, silently
// loading no styles (same trap documented on Minigame).
//
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System;
using System.Linq;
using Sandbox;

namespace Sandbox.UI;

public partial class Hud : PanelComponent
{
	/// <summary>
	/// The scene's GameManager, the source of the local player's PlayerProgress/DrunkCC.
	/// Set this explicitly in the scene (do NOT rely on GetComponent on this panel's own
	/// GameObject - the HUD panel and GameManager live on different objects).
	/// </summary>
	[Property] private GameManager _GameManager { get; set; }

	/// <summary>Below this many seconds the timer text turns orange (Design Screen 1).</summary>
	[Property] private float _TimerWarnSeconds { get; set; } = 30f;

	/// <summary>Below this many seconds the timer text flashes (Design Screen 1).</summary>
	[Property] private float _TimerCriticalSeconds { get; set; } = 10f;

	/// <summary>
	/// Build/version watermark shown bottom-right (any text; empty string hides it).
	/// No version field exists in the .sbproj, so this is the single source of truth -
	/// bump it here (or override in the scene inspector) when shipping a build.
	/// </summary>
	[Property] private string _VersionLabel { get; set; } = "v0.2.0";

	/// <summary>
	/// How many times per second the HUD text rebuilds (default 4). The visible BuildHash
	/// branch quantizes RealTime.Now by this rate, throttling the repaint down from every
	/// frame so score/beer digits tick calmly instead of churning. CSS animations (timer
	/// flash, pants blink) are compositor-driven and unaffected. Values &lt;= 0 are clamped
	/// to 1 where used.
	/// </summary>
	[Property] private float _TextRefreshRate { get; set; } = 4f;

	/// <summary>
	/// Seconds of continuous pants-DOWN (local DrunkCC.CurrentPantsState) before the
	/// right-edge pants warning readout appears blinking. Hidden (and the timer reset)
	/// the moment the pants come back up.
	/// </summary>
	[Property] private float _PantsDownWarnSeconds { get; set; } = 2f;

	/// <summary>
	/// Blink rate of the pants warning sign, full on/off cycles per second. Drives the
	/// animation-duration inline style on .pants-debug (step-end keyframe).
	/// </summary>
	[Property] private float _PantsDebugBlinksPerSecond { get; set; } = 6f;

	/// <summary>
	/// The bar "-JustModel" prefabs (Garys / The Drunken Cam / The Spongey Splatoon) used for
	/// the bottom-left target-bar preview. Wire all three in the scene inspector on the UI-Hud
	/// GameObject; each entry must line up BY INDEX with _BarPreviewNames below.
	/// </summary>
	[Property] private GameObject[] _BarPreviewPrefabs { get; set; }

	/// <summary>
	/// The BarModule display Name matching each entry of _BarPreviewPrefabs (same order):
	/// "Garys", "The Drunken Cam", "The Spongey Splatoon". The -JustModel prefabs carry no
	/// BarModule of their own anymore (stripped 09-24), so the pairing is authored here /
	/// in the scene inspector instead of read off the prefab. An unmatched or empty pairing
	/// just logs a warning and shows nothing.
	/// </summary>
	[Property] private string[] _BarPreviewNames { get; set; }

	/// <summary>Preview turntable spin speed, degrees per second.</summary>
	[Property] private float _PreviewSpinSpeed { get; set; } = 24f;

	/// <summary>
	/// Preview "kilter": static roll tilt (degrees) applied to the turntable root so the bar
	/// sits slightly off-level while spinning. 0 = upright.
	/// </summary>
	[Property] private float _PreviewKilter { get; set; } = 10f;

	// Cached each frame in OnUpdate so the render-tree accessors below don't each re-resolve.
	private PlayerProgress _progress;
	private DrunkCC _drunk;

	// Continuous seconds the local player's pants have been DOWN (accumulated in OnUpdate
	// off RealTime.Delta; reset the moment they're up or the DrunkCC is unresolved).
	// Drives the automatic pants-warning gate (PantsDebugShown).
	private float _pantsDownSeconds;

	/// <summary>
	/// One row of the top-left live player list column: display name, floored live score and
	/// whether this is the local player's row (pinned first, magenta name).
	/// </summary>
	private readonly struct PlayerListRow
	{
		public string Name { get; }
		public int Score { get; }
		public bool IsLocal { get; }

		public PlayerListRow( string name, int score, bool isLocal )
		{
			Name = name;
			Score = score;
			IsLocal = isLocal;
		}
	}

	// Live player-list rows (top-left column), rebuilt every frame in UpdatePlayerListHelper.
	// Initialized empty so the razor @foreach never iterates null before the first tick.
	private List<PlayerListRow> _playerRows = new();

	// Target-bar preview state. _previewHost is the @ref-captured <scene> element (null while
	// the Visible gate has it unrendered - the engine nulls @refs when their block is
	// destroyed). The rest caches what was built INTO the host's private RenderScene so a
	// rebuild only happens when the host panel is recreated (leaving/returning to the run)
	// or the target bar changes. The RenderScene is owned by ScenePanel and destroyed with it,
	// so there is nothing to clean up here beyond dropping the references. _previewFramed
	// latches the auto-frame: ModelRenderer.Bounds falls back to a 16-unit box while the
	// model resource is still loading, so framing retries each frame until the real bounds
	// resolve (the turntable stays still until then, keeping the measurement at rest pose).
	private ScenePanel _previewHost;
	private Scene _previewScene;
	private GameObject _previewPivot;   // spin node (child of the kilter-tilt node)
	private GameObject _previewTiltGo;
	private GameObject _previewCamGo;
	private GameObject _previewClone;
	private CameraComponent _previewCamera;
	private string _previewBarName;
	private float _previewSpin;
	private bool _previewFramed;

	protected override void OnStart()
	{
		if ( _GameManager is null )
			Log.Warning( "Hud: _GameManager is not wired - the HUD will stay hidden. Set the GameManager property on the UI-Hud GameObject in the scene." );
	}

	protected override void OnUpdate()
	{
		_progress = _GameManager?.GetLocalPlayerProgress();
		_drunk = _GameManager?.GetLocalDrunkCC();

		// Continuous pants-down timer for the automatic warning (RealTime, not Time:
		// UI clock, unscaled, matching the rest of the HUD's timing).
		if ( _drunk is null || _drunk.CurrentPantsState != DrunkCC.PantsState.Down )
			_pantsDownSeconds = 0f;
		else
			_pantsDownSeconds += RealTime.Delta;

		UpdatePlayerListHelper();

		UpdateTargetBarPreviewHelper();
	}

	/// <summary>
	/// Rebuilds _playerRows (top-left live player list column) from every tagged "player" body in
	/// the scene. Exactly one row per body - remote player bodies replicate to all clients,
	/// so the enumeration itself is the roster. Deliberately NOT filtered through the
	/// project's net.IsMine() extension: it returns true for EVERY proxy when networking is
	/// inactive, which would double-list bodies. A body counts when it has an owner
	/// connection, or (solo/editor plain-clone case: no NetworkObject, so no connections at
	/// all) when its PlayerProgress is the local one already cached in _progress.
	/// Read-only: touches no state on PlayerProgress/Connection. Called every frame - the
	/// throttled visible BuildHash branch (_TextRefreshRate) still repaints often enough
	/// for the list to track live [Sync] scores without any extra invalidation logic.
	/// Sort: local row pinned first, then score descending, ties by name (Ordinal).
	/// </summary>
	private void UpdatePlayerListHelper()
	{
		// The list only renders while the HUD body does; skip the whole-scene tag scan
		// and row allocations while hidden (menu, bar screens, minigame - most of a
		// session). Safe: OnUpdate runs before the hash-driven repaint, so the first
		// visible frame rebuilds the rows before they render.
		if ( !Visible )
			return;

		var rows = new List<PlayerListRow>();

		// One null-guarded read: Connection.Local is a fake local connection and can be
		// null in editor contexts (same guard idiom as MainMenu's leaderboard rows).
		var localConnection = Connection.Local;

		foreach ( var body in Scene.Scene.FindAllWithTagOrigin( "player" ) )
		{
			if ( !body.IsValid() )
				continue;

			var progress = body.GetComponent<PlayerProgress>();
			if ( progress is null || !progress.IsValid() )
				continue;

			var net = body.Network;
			// Defensive null-check on the accessor itself (same guard Extensions.IsMine
			// carries): null or inactive Network => no connections, the solo plain-clone case.
			bool netActive = net is not null && net.Active;
			var owner = netActive ? net.Owner : null;

			bool isLocal = localConnection is not null && (owner?.Id ?? Guid.Empty) == localConnection.Id;

			if ( owner is null )
			{
				// No owner: either a proxy/unowned netted body (skip - its owner's client
				// lists it under its own connection elsewhere) or the solo plain-clone
				// (no NetworkObject at all). Only the clone of OUR local player counts,
				// and it is by definition the local row even when Local is null.
				if ( netActive || !ReferenceEquals( progress, _progress ) )
					continue;

				isLocal = true;
			}

			// Remote rows use their owner's DisplayName; only the solo-clone row falls
			// back to the local connection's name (null in some editor contexts).
			string name = owner is not null ? owner.DisplayName : localConnection?.DisplayName;
			if ( string.IsNullOrWhiteSpace( name ) )
				name = "PLAYER";

			rows.Add( new PlayerListRow( name, (int)MathF.Floor( progress.Score ), isLocal ) );
		}

		_playerRows = rows
			.OrderByDescending( r => r.IsLocal )
			.ThenByDescending( r => r.Score )
			.ThenBy( r => r.Name, StringComparer.Ordinal )
			.ToList();
	}

	/// <summary>
	/// Keeps the bottom-left target-bar preview alive: rebuilds the ScenePanel's private scene
	/// when the &lt;scene&gt; element was recreated (run gate re-entry) or the target bar
	/// changed, otherwise spins the turntable and keeps the camera's render size matched to the
	/// panel. Runs every frame; all the expensive work is inside the rebuild branch, which only
	/// fires on those transitions (at most once per transition even when the target name is
	/// transiently null - the name mismatch flips _previewBarName in the same pass). Null-target
	/// frames (sparse [Sync] _Bars not replicated yet, ActiveBarModule unresolved) simply retry
	/// next frame.
	/// </summary>
	private void UpdateTargetBarPreviewHelper()
	{
		var host = _previewHost;
		if ( host is null || !host.IsValid() )
		{
			// Gate closed (not in a live run): the panel and its owned RenderScene are gone.
			ClearTargetBarPreviewCacheHelper();
			return;
		}

		string targetName = _GameManager?.GetTargetBar()?.ActiveBarModule?.Name;

		bool hostChanged = _previewScene is null
			|| !_previewScene.IsValid()
			|| !ReferenceEquals( _previewScene, host.RenderScene );
		if ( hostChanged )
		{
			_previewScene = null;
			_previewBarName = null;
		}

		if ( _previewScene is null && targetName is null )
			return; // nothing to show yet - retry once the target bar resolves

		if ( _previewScene is null || targetName != _previewBarName )
			RebuildTargetBarPreviewHelper( host, targetName );
		else
			TickTargetBarPreviewHelper( host );
	}

	/// <summary>
	/// (Re)builds the preview scene contents from scratch: clears every root object of the
	/// ScenePanel's private scene, then creates the camera, two directional lights (the private
	/// scene has no env/sky light), the kilter-tilt &gt; spin rig, and a clone of the target
	/// bar's -JustModel prefab parented under the spin node. The clone starts DISABLED, is
	/// stripped to a pure visual (colliders, NavMeshArea, BarModule, the disabled Enter-Ring
	/// particle node, and network modes all removed/neutralized - gameplay components have no
	/// business in a UI-owned scene), and only then enabled. Framing is NOT done here:
	/// ModelRenderer.Bounds returns a 16-unit fallback until the model resource loads, so
	/// TickTargetBarPreviewHelper retries FramePreviewHelper each frame until the real bounds
	/// resolve (see _previewFramed).
	/// </summary>
	private void RebuildTargetBarPreviewHelper( ScenePanel host, string barName )
	{
		var scene = host.RenderScene;
		if ( scene is null || !scene.IsValid() )
			return;

		// Clear previous contents (old model, camera, lights, rig).
		foreach ( var child in scene.Children.ToList() )
			child.Destroy();

		_previewScene = scene;
		_previewBarName = barName;
		_previewPivot = null;
		_previewTiltGo = null;
		_previewCamGo = null;
		_previewClone = null;
		_previewCamera = null;
		_previewSpin = 0f;
		_previewFramed = false;

		var prefab = FindBarPreviewPrefabHelper( barName );
		if ( barName is not null && prefab is null )
			Log.Warning( $"Hud: no -JustModel prefab paired with target bar name '{barName}' in _BarPreviewNames/_BarPreviewPrefabs; the target-bar preview will be empty." );

		using ( scene.Push() )
		{
			var camGo = scene.CreateObject();
			camGo.Name = "bar-preview-camera";
			var cam = camGo.Components.Create<CameraComponent>();
			cam.FieldOfView = 45f;
			cam.BackgroundColor = Color.Transparent;
			cam.ZNear = 1f;
			// IsMainCamera forces Scene.UpdateMainCamera() synchronously at creation, so
			// ScenePanel's RenderScene.Camera branch resolves to THIS camera immediately
			// instead of waiting for the next enable/priority shuffle.
			cam.IsMainCamera = true;
			// Default pose until FramePreviewHelper lands the auto-frame (a rig with no
			// model must still get a sane camera instead of one sitting at the origin).
			camGo.WorldPosition = PreviewCamDir * 256f;
			camGo.WorldRotation = Rotation.LookAt( -PreviewCamDir );
			_previewCamGo = camGo;
			_previewCamera = cam;

			// Key + fill: the private scene has no environment lighting of its own.
			var keyGo = scene.CreateObject();
			keyGo.WorldRotation = Rotation.From( 45f, 30f, 0f );
			keyGo.Components.Create<DirectionalLight>().LightColor = Color.White * 2f;
			var fillGo = scene.CreateObject();
			fillGo.WorldRotation = Rotation.From( 20f, 210f, 0f );
			fillGo.Components.Create<DirectionalLight>().LightColor = Color.White * 0.4f;

			// tilt (static kilter, screen-plane roll) > spin (turntable about local up).
			var tiltGo = scene.CreateObject();
			tiltGo.Name = "bar-preview-tilt";
			var spinGo = scene.CreateObject();
			spinGo.Name = "bar-preview-spin";
			spinGo.Parent = tiltGo;
			_previewTiltGo = tiltGo;
			_previewPivot = spinGo;

			if ( prefab is null || !prefab.IsValid() )
			{
				// Rig only; when the target name resolves, the mismatch triggers a rebuild.
				TickTargetBarPreviewHelper( host );
				return;
			}

			// Clone DISABLED, strip, then enable - gameplay components (colliders etc.) must
			// never get an OnEnable/OnStart tick inside the UI-owned scene, even for one frame.
			var clone = prefab.Clone( global::Transform.Zero, spinGo, false );
			if ( clone is null || !clone.IsValid() )
			{
				Log.Warning( $"Hud: cloning '{barName}' preview prefab failed; the target-bar preview will be empty." );
				TickTargetBarPreviewHelper( host );
				return;
			}

			StripPreviewCloneHelper( clone );
			clone.Enabled = true;
			_previewClone = clone;

			if ( !clone.GetComponentsInChildren<ModelRenderer>( true ).Any() )
				Log.Warning( $"Hud: '{barName}' preview clone has no ModelRenderer; nothing to frame." );
		}

		TickTargetBarPreviewHelper( host );
	}

	/// <summary>
	/// Per-frame preview upkeep: completes the auto-frame once the model's bounds are ready,
	/// advances the turntable spin (held until framed, so the bounds measurement happens at
	/// the rest pose), and keeps the scene camera's render size matched to the panel's layout
	/// box (a null CustomSize makes CameraComponent fall back to full-screen aspect,
	/// distorting the square preview).
	/// </summary>
	private void TickTargetBarPreviewHelper( ScenePanel host )
	{
		if ( !_previewFramed )
			_previewFramed = FramePreviewHelper();

		if ( _previewFramed && _previewPivot is not null && _previewPivot.IsValid() )
		{
			_previewSpin += _PreviewSpinSpeed * RealTime.Delta;
			_previewPivot.LocalRotation = Rotation.FromAxis( Vector3.Up, _previewSpin );
		}

		if ( _previewCamera is not null && _previewCamera.IsValid() )
		{
			var size = host.Box.RectInner.Size;
			if ( size.x > 0f && size.y > 0f )
				_previewCamera.CustomSize = size;
		}
	}

	/// <summary>Camera direction for the preview shot: mostly -X, slightly above and to the side.</summary>
	private static readonly Vector3 PreviewCamDir = new Vector3( -1f, 0.25f, 0.18f ).Normal;

	/// <summary>
	/// Auto-frames the preview ONCE: centers the clone on the spin origin and backs the camera
	/// off along the bounds diagonal so the whole bar fits with a margin, then applies the
	/// kilter tilt last (framing measures an untilted model centered at origin). Returns false
	/// while the model resource hasn't loaded yet - ModelRenderer.Bounds falls back to a
	/// 16-unit box when Model is null (ModelRenderer.Bounds.cs), and framing off that would
	/// park the camera inside the bar permanently. Called every frame until it succeeds.
	/// </summary>
	private bool FramePreviewHelper()
	{
		if ( _previewClone is null || !_previewClone.IsValid() )
			return true; // no model to frame; the default camera pose stands

		var renderers = _previewClone.GetComponentsInChildren<ModelRenderer>( true ).ToList();
		if ( renderers.Count == 0 )
			return true; // warned at rebuild; nothing will ever load to frame

		// Bounds are only trustworthy once every renderer's Model resource has resolved.
		foreach ( var r in renderers )
		{
			if ( r.Model is null )
				return false;
		}

		var bounds = BBox.FromBoxes( renderers.Select( r => r.Bounds ) );
		if ( bounds.Size.Length < 32f )
			return false; // still the fallback-box signature; retry next frame

		_previewClone.LocalPosition = -bounds.Center;

		float halfFovRad = _previewCamera.FieldOfView * 0.5f * MathF.PI / 180f;
		float distance = MathF.Max( ( bounds.Size.Length * 0.5f ) / MathF.Tan( halfFovRad ) * 1.2f, 64f );
		_previewCamGo.WorldPosition = PreviewCamDir * distance;
		_previewCamGo.WorldRotation = Rotation.LookAt( -PreviewCamDir );

		// Apply the kilter LAST so framing measured an untilted model centered at origin.
		_previewTiltGo.WorldRotation = Rotation.FromRoll( _PreviewKilter );
		return true;
	}

	/// <summary>Drops all cached preview references (gate closed; ScenePanel owns/destroys the scene).</summary>
	private void ClearTargetBarPreviewCacheHelper()
	{
		_previewScene = null;
		_previewPivot = null;
		_previewTiltGo = null;
		_previewCamGo = null;
		_previewClone = null;
		_previewCamera = null;
		_previewBarName = null;
		_previewFramed = false;
	}

	/// <summary>
	/// Finds the -JustModel prefab paired (by index) with the target bar's
	/// ActiveBarModule.Name in _BarPreviewNames/_BarPreviewPrefabs. Returns null when
	/// nothing is wired/matches (caller warns).
	/// </summary>
	private GameObject FindBarPreviewPrefabHelper( string barName )
	{
		if ( string.IsNullOrEmpty( barName ) || _BarPreviewPrefabs is null || _BarPreviewNames is null )
			return null;

		for ( int i = 0; i < _BarPreviewNames.Length && i < _BarPreviewPrefabs.Length; i++ )
		{
			if ( _BarPreviewNames[i] != barName )
				continue;

			var prefab = _BarPreviewPrefabs[i];
			if ( prefab is not null && prefab.IsValid() )
				return prefab;
		}

		return null;
	}

	/// <summary>
	/// Strips the gameplay pieces the -JustModel prefabs carry (static colliders, NavMeshArea,
	/// BarModule, the disabled Enter-Ring particle node) and neutralizes network modes across
	/// the hierarchy. The preview clone lives in a UI-owned private scene with no physics/
	/// navmesh/network systems; leaving them on risks warnings and pointless per-scene physics
	/// world creation, and snapshot-mode nodes have no meaning outside the game scene.
	/// </summary>
	private static void StripPreviewCloneHelper( GameObject clone )
	{
		foreach ( var c in clone.GetComponentsInChildren<Collider>( true ).ToList() )
			c.Destroy();
		foreach ( var c in clone.GetComponentsInChildren<NavMeshArea>( true ).ToList() )
			c.Destroy();
		foreach ( var c in clone.GetComponentsInChildren<BarModule>( true ).ToList() )
			c.Destroy();
		foreach ( var c in clone.GetComponentsInChildren<ParticleEffect>( true ).ToList() )
			c.Destroy();

		// The Enter-Ring node (Spongey) is particle-only; drop the whole child.
		var ring = clone.Children.FirstOrDefault( c => c.Name.Contains( "Enter-Ring", StringComparison.OrdinalIgnoreCase ) );
		ring?.Destroy();

		NeutralizeNetworkHelper( clone );
	}

	private static void NeutralizeNetworkHelper( GameObject go )
	{
		go.NetworkMode = NetworkMode.Never;
		foreach ( var child in go.Children )
			NeutralizeNetworkHelper( child );
	}

	/// <summary>
	/// True only while the local client is pub-crawling on the street - false in the main
	/// menu, while inside a bar (AtBarMenu decision, WaitingToStartMinigame, PlayingMinigame),
	/// and fail-closed if GameManager is unwired. Gates the HUD body (nothing may sit behind
	/// the BarMenu modal or the minigame overlay) and the pants-debug column (it used to only
	/// exclude the menu, so it floated over bar screens too).
	/// </summary>
	private bool IsPubCrawling => _GameManager?.GetLocalGameState() == GameManager.LocalGameState.PubCrawling;

	/// <summary>
	/// Shown once the player exists AND the run is live (Running or Paused) AND the local
	/// client is on the street (IsPubCrawling). Hidden before spawn, before the first bar
	/// starts the run (PreRun), after Ended, and while IN A BAR: the BarMenu decision and
	/// the minigame own the screen, so the HUD must not sit behind them. RunState alone
	/// can't express this - the timer is Paused for the whole bar visit, which is exactly
	/// the window to hide. Design.md Screen 2 puts the paused timer inside the BarMenu
	/// modal's own corner spec (not yet built there), not on a dimmed HUD underneath it.
	/// </summary>
	private bool Visible
	{
		get
		{
			if ( _progress is null )
				return false;

			if ( !IsPubCrawling )
				return false;

			var state = _progress.RunState;
			return state == PlayerProgress.RunStateEnum.Running
				|| state == PlayerProgress.RunStateEnum.Paused;
		}
	}

	/// <summary>Design #45: timer freezes in the bar - dim the HUD and show a paused banner.</summary>
	private bool IsPaused => _progress?.RunState == PlayerProgress.RunStateEnum.Paused;

	private float TimerSeconds => MathF.Max( 0f, _progress?.Timer ?? 0f );

	/// <summary>Design Screen 1: M:SS, e.g. 2:00. Floors partial seconds so 119.6s reads 1:59.</summary>
	private string TimerText
	{
		get
		{
			int total = (int)MathF.Floor( TimerSeconds );
			int minutes = total / 60;
			int seconds = total % 60;
			return $"{minutes}:{seconds:D2}";
		}
	}

	/// <summary>orange under _TimerWarnSeconds, flashing under _TimerCriticalSeconds.</summary>
	private string TimerClass
	{
		get
		{
			if ( TimerSeconds < _TimerCriticalSeconds )
				return "critical";
			if ( TimerSeconds < _TimerWarnSeconds )
				return "warning";
			return "";
		}
	}

	/// <summary>Design Screen 1: one-decimal beer count, e.g. 3.5.</summary>
	private string BeersText => ( _drunk?.BeerLevel ?? 0f ).ToString( "F2" );

	/// <summary>
	/// DEBUG: raw synced pants state of the local DrunkCC, e.g. "PANTS: UP".
	/// Shows "NO PLAYER" until the local DrunkCC resolves, so a fake UP default
	/// can't be mistaken for a real sync value.
	/// </summary>
	private string PantsDebugText => _drunk is null ? "NO PLAYER"
		: _drunk.CurrentPantsState == DrunkCC.PantsState.Down ? "PULL YOUR PANTS UP"
		: "PANTS: UP";

	/// <summary>DEBUG: true colour so a stuck state is obvious at a glance (Up=green, Down=red).</summary>
	private bool PantsDebugIsDown => _drunk?.CurrentPantsState == DrunkCC.PantsState.Down;

	/// <summary>
	/// Automatic gate for the right-edge pants warning: hidden by default; appears once the
	/// local DrunkCC's pants have been continuously DOWN for _PantsDownWarnSeconds, and hides
	/// (the timer resetting) the moment the pants come back up. Never shows off-street
	/// (IsPubCrawling) or before the local DrunkCC resolves.
	/// </summary>
	private bool PantsDebugShown => IsPubCrawling && _drunk is not null && _pantsDownSeconds >= _PantsDownWarnSeconds;

	/// <summary>
	/// One full blink cycle of the pants warning in seconds (1 / _PantsDebugBlinksPerSecond,
	/// floored at 0.1 blinks/s so a bogus property can't produce an infinite/zero duration).
	/// Invariant-culture formatted: it lands verbatim in an inline CSS animation-duration, so
	/// a comma-decimal locale must never leak through.
	/// </summary>
	private string PantsBlinkDurationText
	{
		get
		{
			float duration = 1f / MathF.Max( _PantsDebugBlinksPerSecond, 0.1f );
			return duration.ToString( "F3", System.Globalization.CultureInfo.InvariantCulture ) + "s";
		}
	}

	// Repaint throttled to _TextRefreshRate rebuilds/second while visible (RealTime.Now
	// quantized to that rate) so score/beer digits tick calmly instead of churning every
	// frame; CSS animations (timer flash, pants blink) are compositor-driven and unaffected.
	// OnUpdate/UpdatePlayerListHelper still run every frame - only the RENDER is throttled.
	// While hidden, the hash still tracks the pants-warning gate so its text updates while
	// off-street are applied on the next repaint instead of freezing at first paint.
	// IsPubCrawling is in BOTH branches so the menu/bar -> street state flip repaints the
	// gated blocks. Hash to a constant floor while hidden so the idle panel stays put
	// (mirrors Minigame.BuildHash).
	protected override int BuildHash() => Visible
		? HashCode.Combine( Visible, IsPubCrawling, PantsDebugShown, (int)( RealTime.Now * MathF.Max( _TextRefreshRate, 1f ) ) )
		: HashCode.Combine( Visible, IsPubCrawling, PantsDebugShown );
}
