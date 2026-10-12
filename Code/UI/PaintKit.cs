// Project: sboxgamejam3
// File:    PaintKit.cs
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
// Shared building blocks for the code-built UI panels that draw their visuals with Painter
// instead of razor and scss. Holds the palette and font names, the panel placement helpers,
// the fullscreen click-through drawing canvas and the self-painting button.

using System;
using System.Runtime.CompilerServices;

namespace Sandbox.UI;

/// <summary>
/// Painter callback. A delegate (not Action&lt;Painter&gt;) because Painter is a ref struct.
/// </summary>
public delegate void PaintHandler( Painter painter );

/// <summary>
/// The project palette and font families.
/// </summary>
public static class UiTheme
{
	/// <summary>#00166D, background / dusk sky / modal fill.</summary>
	public static readonly Color DeepBlue = Color.FromRgb( 0x00166D );

	/// <summary>#4C74E5, primary glow, default neon edges, borders.</summary>
	public static readonly Color MediumBlue = Color.FromRgb( 0x4C74E5 );

	/// <summary>#FFD51B, timer, score, percentage, accent warm.</summary>
	public static readonly Color Yellow = Color.FromRgb( 0xFFD51B );

	/// <summary>#FF6120, danger, pants warning, game over, accent hot.</summary>
	public static readonly Color Orange = Color.FromRgb( 0xFF6120 );

	/// <summary>#E03AC0, drunkenness, game-over accent, local leaderboard row.</summary>
	public static readonly Color Magenta = Color.FromRgb( 0xE03AC0 );

	/// <summary>Display font: titles and big chunky text.</summary>
	public const string DisplayFont = "First Blood";

	/// <summary>Body font.</summary>
	public const string BodyFont = "Snow Blue";

	/// <summary>Built-in fallback font used by the plain-text screens.</summary>
	public const string PlainFont = "Poppins";

	/// <summary>
	/// Convenience TextStyle builder. Every style it hands out carries TextFlag.DontClip:
	/// Painter.Text lays the text out against the rect it is given, and a line taller than
	/// that rect is dropped entirely, which made the readouts vanish at some resolutions.
	/// DontClip lifts both limits, and Rect.Align only reads the alignment bits.
	/// </summary>
	public static TextStyle Text( string font, float size, Color color, TextFlag align = TextFlag.LeftTop )
	{
		return new TextStyle
		{
			FontName = font,
			FontSize = size,
			Color = color,
			Alignment = align | TextFlag.DontClip,
		};
	}
}

/// <summary>
/// Layout helpers for code-built panels.
/// </summary>
public static class PanelKitExtensions
{
	/// <summary>
	/// Last rect applied to a panel, so PlaceAt can skip redundant style writes.
	/// </summary>
	sealed class Placement
	{
		public bool HasRect;
		public Rect Rect;
	}

	static readonly ConditionalWeakTable<Panel, Placement> Placements = new();

	/// <summary>
	/// Position absolute, Left 0, Top 0, Width 100%, Height 100%.
	/// </summary>
	public static void MakeFullscreen( this Panel panel )
	{
		if ( panel is null ) return;

		var style = panel.Style;
		style.Position = PositionMode.Absolute;
		style.Left = Length.Pixels( 0 );
		style.Top = Length.Pixels( 0 );
		style.Width = Length.Percent( 100 );
		style.Height = Length.Percent( 100 );
	}

	/// <summary>
	/// Position absolute at rect (panel units). Writes Left/Top/Width/Height in pixels.
	/// Skips the style writes when the rect equals the last one applied to this panel.
	/// </summary>
	public static void PlaceAt( this Panel panel, Rect rect )
	{
		if ( panel is null ) return;

		var placement = Placements.GetValue( panel, _ => new Placement() );

		if ( placement.HasRect
			&& placement.Rect.Left == rect.Left
			&& placement.Rect.Top == rect.Top
			&& placement.Rect.Width == rect.Width
			&& placement.Rect.Height == rect.Height )
		{
			return;
		}

		placement.HasRect = true;
		placement.Rect = rect;

		var style = panel.Style;
		style.Position = PositionMode.Absolute;
		style.Left = Length.Pixels( rect.Left );
		style.Top = Length.Pixels( rect.Top );
		style.Width = Length.Pixels( rect.Width );
		style.Height = Length.Pixels( rect.Height );
	}

	/// <summary>
	/// Display Flex when true, None when false. Only writes when the value changes.
	/// </summary>
	public static void SetVisible( this Panel panel, bool visible )
	{
		if ( panel is null ) return;

		var target = visible ? DisplayMode.Flex : DisplayMode.None;
		if ( panel.Style.Display == target ) return;

		panel.Style.Display = target;
	}

	/// <summary>
	/// The panel's current size in panel units: Box.Rect.Size * ScaleFromScreen.
	/// Zero before the first layout pass.
	/// </summary>
	public static Vector2 LocalSize( this Panel panel )
	{
		if ( panel is null ) return Vector2.Zero;

		return panel.Box.Rect.Size * panel.ScaleFromScreen;
	}

	/// <summary>
	/// Panel units to screen pixels. Layout and PlaceAt use panel units (a 1080-tall reference);
	/// Painter draws in screen pixels, so drawn rects are converted with Panel.ScaleToScreen.
	/// </summary>
	public static Rect ToScreen( this Rect rect, float scale )
	{
		return new Rect( rect.Position * scale, rect.Size * scale );
	}
}

/// <summary>
/// Base for the code-built screens: derives from Component rather than PanelComponent,
/// because PanelComponent always loads the stylesheet named after the shortest source
/// file (<c>ui/hud.cs.scss</c>), which these screens do not have - the editor logged
/// "Error opening stylesheet" on every enable. Instead this owns a plain Panel parented
/// to the ScreenPanel's root, which is all the screens ever needed from PanelComponent.
/// </summary>
[Category( "UI Panels" )]
[Icon( "widgets" )]
public abstract class PaintPanelComponent : Component, Component.DontExecuteOnServer
{
	ScreenPanel _screenPanel;
	Panel _panel;

	/// <summary>
	/// The root panel this screen builds into, a child of the ScreenPanel's root on the same
	/// GameObject. Created on first access while enabled, recreated if the ScreenPanel rebuilt its
	/// root, deleted on disable/destroy. Null while disabled or before the ScreenPanel has a root.
	/// </summary>
	public Panel Panel
	{
		get
		{
			EnsureRootHelper();
			return _panel;
		}
	}

	/// <summary>
	/// Creates the root panel under the ScreenPanel's root, or re-parents it when that root was
	/// rebuilt. The ScreenPanel only re-parents its own PanelComponent-derived children on enable,
	/// so this resolves its own root lazily on every access instead.
	/// </summary>
	void EnsureRootHelper()
	{
		if ( !Active ) return;
		if ( !_screenPanel.IsValid() ) _screenPanel = Components.Get<ScreenPanel>();
		var root = _screenPanel?.GetPanel();
		if ( root is null || !root.IsValid() ) return;
		if ( _panel.IsValid() && _panel.Parent == root ) return;
		if ( _panel.IsValid() ) _panel.Delete();
		_panel = new Panel();
		_panel.ElementName = GetType().Name.ToLowerInvariant();
		_panel.Parent = root;
	}

	protected override void OnDisabled() => DestroyRootHelper();
	protected override void OnDestroy() => DestroyRootHelper();

	void DestroyRootHelper()
	{
		if ( _panel.IsValid() ) _panel.Delete();
		_panel = null;
	}
}

/// <summary>
/// Full-size, click-through drawing surface. OnDraw calls Draw.
/// </summary>
public class PaintCanvas : Panel
{
	/// <summary>
	/// Everything this canvas paints. Assign it before adding the canvas to a tree.
	/// Hides the legacy static Panel.Draw helper class, which this canvas never uses.
	/// </summary>
	public new PaintHandler Draw { get; set; }

	public PaintCanvas()
	{
		this.MakeFullscreen();
		Style.PointerEvents = PointerEvents.None;
	}

	public override void OnDraw( Painter painter )
	{
		Draw?.Invoke( painter );
	}
}

/// <summary>
/// Clickable panel that paints itself. Position it with PlaceAt each update.
/// </summary>
public class PaintButton : Panel
{
	/// <summary>
	/// Centered label. Leave empty to draw the button without text.
	/// </summary>
	public string Label { get; set; }

	/// <summary>
	/// Invoked on a left mouse click release while hovering.
	/// </summary>
	public Action Clicked { get; set; }

	/// <summary>
	/// The button's look. Defaults to <see cref="PaintButtonLook.Default"/>.
	/// </summary>
	public PaintButtonLook Look { get; set; } = PaintButtonLook.Default;

	/// <summary>
	/// Whether the mouse is currently over this button.
	/// </summary>
	public bool IsHovered => HasHovered;

	public PaintButton()
	{
		Style.Position = PositionMode.Absolute;
		Style.PointerEvents = PointerEvents.All;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Clicked?.Invoke();
	}

	public override void OnDraw( Painter painter )
	{
		var look = Look ?? PaintButtonLook.Default;

		if ( look.CustomDraw is not null )
		{
			look.CustomDraw( this, painter );
			return;
		}

		if ( HasActive )
		{
			using ( painter.Scope() )
			{
				painter.Opacity = look.PressedOpacity;
				DrawDefaultHelper( painter, look, HasHovered );
			}

			return;
		}

		DrawDefaultHelper( painter, look, HasHovered );
	}

	/// <summary>
	/// The default button look: rounded rect plus a centered label. Painter.Bounds is in screen
	/// pixels, so the look's panel-unit geometry (border width, corner radius) is multiplied by
	/// ScaleToScreen before it is drawn. The label font size is NOT scaled: Painter.Text already
	/// multiplies TextStyle.FontSize by ScaleToScreen when it builds the text.
	/// </summary>
	void DrawDefaultHelper( Painter painter, PaintButtonLook look, bool hovered )
	{
		using ( painter.Scope() )
		{
			float s = ScaleToScreen;
			var bounds = painter.Bounds;

			painter.Fill = hovered ? look.HoverFill : look.Fill;
			painter.Stroke = look.BorderWidth > 0f
				? Stroke.Solid( hovered ? look.HoverBorder : look.Border, look.BorderWidth * s )
				: Stroke.None;
			painter.Rect( bounds, new Painter.CornerRadii( look.CornerRadius * s ) );

			if ( string.IsNullOrEmpty( Label ) ) return;

			painter.Stroke = Stroke.None;
			painter.TextStyle = new TextStyle
			{
				FontName = look.Font,
				FontSize = look.FontSize,
				Color = hovered ? look.HoverTextColor : look.TextColor,
				Alignment = TextFlag.Center | TextFlag.DontClip,
			};
			painter.Text( Label, bounds );
		}
	}
}

/// <summary>
/// Optional full override: draw the whole button yourself (Painter is a ref struct, so this is
/// a delegate type, not Action&lt;...&gt;).
/// </summary>
public delegate void PaintButtonDraw( PaintButton button, Painter painter );

/// <summary>
/// The look of a <see cref="PaintButton"/>. Immutable, build one with an object initializer.
/// </summary>
public sealed class PaintButtonLook
{
	/// <summary>Fill while not hovered.</summary>
	public Color Fill { get; init; }

	/// <summary>Fill while hovered.</summary>
	public Color HoverFill { get; init; }

	/// <summary>Border color while not hovered.</summary>
	public Color Border { get; init; }

	/// <summary>Border color while hovered.</summary>
	public Color HoverBorder { get; init; }

	/// <summary>Label color while not hovered.</summary>
	public Color TextColor { get; init; }

	/// <summary>Label color while hovered.</summary>
	public Color HoverTextColor { get; init; }

	/// <summary>Border width in panel units. Zero skips the border.</summary>
	public float BorderWidth { get; init; } = 2f;

	/// <summary>Corner radius in panel units. Zero is a sharp rect.</summary>
	public float CornerRadius { get; init; } = 0f;

	/// <summary>Label font family.</summary>
	public string Font { get; init; } = UiTheme.DisplayFont;

	/// <summary>Label font size in panel units.</summary>
	public float FontSize { get; init; } = 32f;

	/// <summary>Opacity while the mouse button is held down on the button.</summary>
	public float PressedOpacity { get; init; } = 0.8f;

	/// <summary>
	/// Optional full override: draw the whole button yourself (Painter is a ref struct, so this is
	/// a delegate type, not Action&lt;...&gt;).
	/// </summary>
	public PaintButtonDraw CustomDraw { get; init; }

	/// <summary>
	/// MediumBlue border, transparent fill, Yellow hover text, white text, DisplayFont 32.
	/// </summary>
	public static PaintButtonLook Default { get; } = new PaintButtonLook
	{
		Fill = Color.Transparent,
		HoverFill = UiTheme.MediumBlue.WithAlpha( 0.25f ),
		Border = UiTheme.MediumBlue,
		HoverBorder = UiTheme.Yellow,
		TextColor = Color.White,
		HoverTextColor = UiTheme.Yellow,
		BorderWidth = 2f,
		CornerRadius = 0f,
		Font = UiTheme.DisplayFont,
		FontSize = 32f,
		PressedOpacity = 0.8f,
	};
}
