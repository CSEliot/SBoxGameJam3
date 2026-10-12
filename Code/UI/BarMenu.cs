// Project: sboxgamejam3
// File:    BarMenu.cs
// Author:  cseliot
// Created: 2026.10.12
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// "One more round?" bar menu (Design.md, Screen 2). Opened by GameManager when
// the player enters a bar trigger AFTER their run has started - the starting
// bar (RunState still PreRun, nothing to cash out yet) skips straight to the
// mini-game as before; see GameManager.HandlePlayerBarTriggerEnter. Lets the
// player choose to play the drinking mini-game again or cash in their score
// and end the run (PlayerProgress.EndRun). Self-contained like EscMenu: no game
// state here beyond IsOpen/ScoreAtOpen, so there is no partial-class
// stylesheet-naming trap to worry about (see Minigame.Logic.cs's header).
//
// The screen builds its panel tree in code and draws all of its visuals
// (backdrop, modal card, title, score line) with Painter; the two choices are
// PaintButtons so hover, click routing and the mouse cursor keep working. There
// is no markup file and no stylesheet for this screen.
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
using Sandbox;

namespace Sandbox.UI;

public class BarMenu : PaintPanelComponent
{
	// Sizes below are the values from the screen's former stylesheet, held in panel
	// units. Layout and PlaceAt both work in panel units. Geometry (rects, stroke widths,
	// radii, shadows) is converted to screen pixels at the painter with Panel.ScaleToScreen;
	// TextStyle font sizes and text shadows stay panel units because the engine scales text itself.
	const float CardWidth = 580f;          // min-width 500 + 40px padding each side
	const float CardHeight = 360f;         // 40 + title 72 + gap 24 + 80 + gap 24 + 80 + 40
	const float CardPadding = 40f;
	const float CardGap = 24f;
	const float CardRadius = 12f;
	const float CardBorderWidth = 2f;
	const float CardFillAlpha = 0.9f;
	const float CardGlowAlpha = 0.5f;
	const float CardGlowBlur = 24f;
	const float BackdropAlpha = 0.8f;
	const float TitleHeight = 72f;
	const float TitleFontSize = 60f;
	const float TitleGlowAlpha = 0.7f;
	const float TitleGlowBlur = 12f;
	const float ButtonWidth = 400f;
	const float ButtonHeight = 80f;
	const float ButtonRadius = 8f;
	const float ButtonBorderWidth = 2f;
	const float ButtonTitleFontSize = 26f;
	const float ButtonSubtitleFontSize = 16f;
	const float ButtonPressedOpacity = 0.8f;

	const string TitleText = "ONE MORE ROUND?";
	const string CashOutTitle = "CASH IN SCORE";
	const string PlayTitle = "PLAY MINI-GAME";
	const string PlaySubtitle = "Drink more, get drunker, get time";

	/// <summary>Root Panel instance the tree below was built into. PaintPanelComponent destroys and
	/// recreates its root Panel on disable/enable, so the tree is rebuilt whenever it differs.</summary>
	Panel _builtFor;

	PaintCanvas _canvas;
	PaintButton _cashOutButton;
	PaintButton _playButton;

	Rect _cardRect;
	Rect _titleRect;
	Rect _cashOutRect;
	Rect _playRect;

	/// <summary>Score the cached cash-out subtitle was built for, so the string is only
	/// reassembled when the snapshot actually changes.</summary>
	float _subtitleScore = float.NaN;
	string _cashOutSubtitle;

	/// <summary>Raised when the player picks "PLAY MINI-GAME". GameManager subscribes and
	/// transitions _localGameState to WaitingToStartMinigame, which auto-starts it.</summary>
	public Action OnPlayMiniGame;

	/// <summary>Raised when the player picks "CASH IN SCORE". GameManager subscribes and
	/// calls PlayerProgress.EndRun(scoreWiped: false).</summary>
	public Action OnCashOut;

	public bool IsOpen { get; private set; }

	/// <summary>
	/// Score snapshot taken when Open() was called, for the cash-out subtitle. A snapshot
	/// pushed in by GameManager rather than a live pull - mirrors Minigame.BeerSpawnLocation
	/// (set externally by Bar.SitDownPlayer), the "push, don't pull" pattern already used for
	/// panels a GameManager transition drives, as opposed to Hud's continuous per-frame pull.
	/// </summary>
	public float ScoreAtOpen { get; private set; }

	/// <summary>Design Screen 2 / 4: 4-digit zero-padded score, matching Hud.ScoreText.</summary>
	private string ScoreText => ( (int)MathF.Floor( ScoreAtOpen ) ).ToString( "D4" );

	/// <summary>Opens the menu with the given score snapshot. Called by GameManager when the
	/// player enters a bar trigger after the run has started.</summary>
	public void Open( float score )
	{
		ScoreAtOpen = score;
		IsOpen = true;
	}

	public void Close()
	{
		IsOpen = false;
	}

	private void PlayMiniGameClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		Close();
		OnPlayMiniGame?.Invoke();
	}

	private void CashOutClicked()
	{
		AudioController.Instance?.PlayUIInteract();
		Close();
		OnCashOut?.Invoke();
	}


	/// <summary>
	/// Places the click targets for the current size. ALSO called from the draw pass so the
	/// drawn card and the hit areas can never drift apart.
	/// </summary>
	void LayoutHelper( Vector2 size )
	{
		var cardLeft = MathF.Floor( ( size.x - CardWidth ) * 0.5f );
		var cardTop = MathF.Floor( ( size.y - CardHeight ) * 0.5f );

		_cardRect = new Rect( cardLeft, cardTop, CardWidth, CardHeight );
		_titleRect = new Rect( cardLeft + CardPadding, cardTop + CardPadding, CardWidth - CardPadding * 2f, TitleHeight );

		var buttonLeft = cardLeft + ( CardWidth - ButtonWidth ) * 0.5f;
		var buttonTop = cardTop + CardPadding + TitleHeight + CardGap;

		_cashOutRect = new Rect( buttonLeft, buttonTop, ButtonWidth, ButtonHeight );
		_playRect = new Rect( buttonLeft, buttonTop + ButtonHeight + CardGap, ButtonWidth, ButtonHeight );
	}

	/// <summary>
	/// Builds the panel tree (drawing canvas + the two buttons) into the current root Panel.
	/// Called every frame before anything else, and a no-op until the root Panel changes.
	/// </summary>
	void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) ) return;

		_builtFor = Panel;

		Panel.MakeFullscreen();

		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );

		// Added after the canvas so the buttons sit on top of it.
		_cashOutButton = new PaintButton
		{
			Clicked = CashOutClicked,
			Look = new PaintButtonLook { CustomDraw = DrawButtonHelper },
		};
		Panel.AddChild( _cashOutButton );

		_playButton = new PaintButton
		{
			Clicked = PlayMiniGameClicked,
			Look = new PaintButtonLook { CustomDraw = DrawButtonHelper },
		};
		Panel.AddChild( _playButton );
	}

	protected override void OnUpdate()
	{
		EnsureTreeHelper();

		if ( IsOpen && ScoreAtOpen != _subtitleScore )
		{
			_subtitleScore = ScoreAtOpen;
			_cashOutSubtitle = $"End run, save {ScoreText} points";
		}

		Panel.SetVisible( IsOpen );

		// Full-screen modal: the cursor shows while it is up and clicks cannot fall
		// through to the screen behind it.
		Panel.Style.PointerEvents = IsOpen ? PointerEvents.All : PointerEvents.None;

		_cashOutButton.SetVisible( IsOpen );
		_playButton.SetVisible( IsOpen );

		if ( !IsOpen ) return;

		LayoutHelper( Panel.LocalSize() );

		_cashOutButton.PlaceAt( _cashOutRect );
		_playButton.PlaceAt( _playRect );
	}

	/// <summary>Paints the backdrop, the modal card and the title.</summary>
	void DrawHelper( Painter painter )
	{
		if ( !IsOpen ) return;

		// Painter draws in screen pixels; layout and PlaceAt work in panel units.
		var s = Panel.ScaleToScreen;
		var bounds = painter.Bounds;
		LayoutHelper( bounds.Size / s );

		using ( painter.Scope() )
		{
			// Backdrop: deep blue wash over the whole screen. The old blur behind it is
			// approximated by this tint instead of a real backdrop filter.
			painter.Fill = UiTheme.DeepBlue.WithAlpha( BackdropAlpha );
			painter.Stroke = Stroke.None;
			painter.Rect( bounds );

			// Card: soft medium-blue glow, then the deep blue fill with its border.
			painter.RectShadow( _cardRect.ToScreen( s ), new Painter.CornerRadii( CardRadius * s ), color: UiTheme.MediumBlue.WithAlpha( CardGlowAlpha ), blur: CardGlowBlur * s );

			painter.Fill = UiTheme.DeepBlue.WithAlpha( CardFillAlpha );
			painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, CardBorderWidth * s );
			painter.Rect( _cardRect.ToScreen( s ), new Painter.CornerRadii( CardRadius * s ) );

			painter.Stroke = Stroke.None;
			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, TitleFontSize, UiTheme.Yellow, TextFlag.Center )
				.WithShadow( UiTheme.Yellow.WithAlpha( TitleGlowAlpha ), TitleGlowBlur, Vector2.Zero );
			painter.Text( TitleText, _titleRect.ToScreen( s ) );
		}
	}

	/// <summary>
	/// Paints either choice: border and text wear the button's accent color (medium blue
	/// for cashing in, magenta for the mini-game), and hovering floods the button with that
	/// accent and turns both lines yellow with a glow.
	/// </summary>
	void DrawButtonHelper( PaintButton button, Painter painter )
	{
		var isPlay = ReferenceEquals( button, _playButton );
		var accent = isPlay ? UiTheme.Magenta : UiTheme.MediumBlue;
		var title = isPlay ? PlayTitle : CashOutTitle;
		var subtitle = isPlay ? PlaySubtitle : _cashOutSubtitle;

		var hovered = button.IsHovered;

		// The button panel's bounds are already screen pixels. Geometry (border, radius,
		// offsets) is converted with ScaleToScreen; TextStyle font sizes and text shadows
		// stay panel units because the engine scales text itself.
		var s = button.ScaleToScreen;

		using ( painter.Scope() )
		{
			if ( button.HasActive )
			{
				painter.Opacity = ButtonPressedOpacity;
			}

			var bounds = painter.Bounds;

			painter.Fill = hovered ? accent : Color.Transparent;
			painter.Stroke = Stroke.Solid( accent, ButtonBorderWidth * s );
			painter.Rect( bounds, new Painter.CornerRadii( ButtonRadius * s ) );

			var textColor = hovered ? UiTheme.Yellow : accent;
			var glow = hovered ? UiTheme.Yellow.WithAlpha( TitleGlowAlpha ) : default( Color? );
			var glowBlur = hovered ? 10f : 0f;

			painter.Stroke = Stroke.None;

			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, ButtonTitleFontSize, textColor, TextFlag.Center )
				.WithShadow( glow, glowBlur, Vector2.Zero );
			painter.Text( title, new Rect( bounds.Left, bounds.Top + 16f * s, bounds.Width, 34f * s ) );

			painter.TextStyle = UiTheme.Text( UiTheme.BodyFont, ButtonSubtitleFontSize, textColor, TextFlag.Center )
				.WithShadow( glow, glowBlur, Vector2.Zero );
			painter.Text( subtitle, new Rect( bounds.Left, bounds.Top + 50f * s, bounds.Width, 20f * s ) );
		}
	}
}
