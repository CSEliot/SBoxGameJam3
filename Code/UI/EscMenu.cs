// Project: sboxgamejam3
// File:    EscMenu.cs
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
// Escape/pause menu overlay. Toggles with ESC. Return to game, leave to
// main menu, open s&box settings.
// Opening is suppressed while the local client is in
// GameManager.LocalGameState.InMainMenu (the main menu, now merged into this
// scene - both are ZIndex 100 full-screen modals and must not stack); closing
// an already-open pause menu still works.
// The panel tree is built in code and every visual is painted with Painter
// (no razor markup, no scss stylesheet, no BuildHash).

using System;

namespace Sandbox.UI;

/// <summary>
/// Escape/pause menu overlay. Backdrop, card, title and the three buttons are drawn
/// with Painter every frame; the buttons are real panels so clicks and hover work.
/// </summary>
public class EscMenu : PaintPanelComponent
{
	/// <summary>Whether the pause menu is currently open.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Optional ref to the scene's BarMenu panel. When set, the pause menu refuses to open
	/// while BarMenu.IsOpen is true, so the two full-screen modals (identical ZIndex 100,
	/// both pointer-events: all) can't stack with ambiguous click routing. Not required -
	/// null-safe - so existing scenes without this wired keep working exactly as before.
	/// </summary>
	[Property] private BarMenu _BarMenu { get; set; }

	/// <summary>
	/// Optional ref to the scene's GameManager. When set, the pause menu refuses to OPEN
	/// while the local client is in GameManager.LocalGameState.InMainMenu - the main menu
	/// now lives in this scene and both modals share ZIndex 100 with pointer-events: all,
	/// so stacking them would give ambiguous click routing. Closing/toggling an already
	/// open pause menu is never blocked by this. Not required - null-safe - so existing
	/// scenes without this wired keep working exactly as before.
	/// </summary>
	[Property] private GameManager _GameManager { get; set; }

	// Layout copied from the old stylesheet, in panel units.
	const float CardWidth = 460f;
	const float CardPadding = 40f;
	const float CardRadius = 12f;
	const float TitleFontSize = 60f;
	const float TitleBoxHeight = 72f;
	// Title margin-bottom (18) plus the column gap (18).
	const float TitleSpacing = 36f;
	const float ButtonWidth = 380f;
	const float ButtonHeight = 72f;
	const float ButtonGap = 18f;
	const float ButtonRadius = 8f;
	const float ButtonFontSize = 26f;
	const float ButtonBorderWidth = 2f;

	const string TitleText = "PAUSED";

	/// <summary>The root Panel instance the code-built tree was created in.</summary>
	Panel _builtFor;

	PaintCanvas _canvas;
	// Invisible full-screen child under the canvas: keeps the old backdrop click-to-close.
	PaintButton _backdropButton;
	// Invisible child over the backdrop, sized to the card: absorbs clicks on the card body
	// and title so only the backdrop outside the card closes the menu, as before.
	PaintButton _cardShieldButton;
	PaintButton _returnButton;
	PaintButton _leaveButton;
	PaintButton _settingsButton;

	Rect _cardRect;
	Rect _titleRect;
	Rect _returnRect;
	Rect _leaveRect;
	Rect _settingsRect;

	/// <summary>
	/// Builds the panel tree into the component's root panel. The root panel is destroyed on
	/// disable and can be recreated, so the tree is rebuilt whenever the instance changes.
	/// </summary>
	void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) ) return;

		_builtFor = Panel;
		Panel.MakeFullscreen();

		// Backdrop first, so the click target sits under the canvas and under the buttons.
		_backdropButton = new PaintButton
		{
			Clicked = CloseMenuHelper,
			Look = new PaintButtonLook
			{
				BorderWidth = 0f,
				Fill = Color.Transparent,
				HoverFill = Color.Transparent,
			},
		};

		Panel.AddChild( _backdropButton );

		_cardShieldButton = new PaintButton
		{
			Look = new PaintButtonLook
			{
				BorderWidth = 0f,
				Fill = Color.Transparent,
				HoverFill = Color.Transparent,
			},
		};

		Panel.AddChild( _cardShieldButton );

		// Then the canvas: backdrop tint, card and title. Click-through, so the backdrop
		// panel below it still receives the clicks outside the card.
		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );

		// Buttons last so they sit on top of the canvas.
		_returnButton = new PaintButton
		{
			Label = "RETURN TO GAME",
			Clicked = CloseMenuHelper,
		};

		_leaveButton = new PaintButton
		{
			Label = "LEAVE TO MAIN MENU",
			Clicked = LeaveToMainMenuHelper,
		};

		_settingsButton = new PaintButton
		{
			Label = "OPEN S&BOX SETTINGS",
			Clicked = OpenSettingsHelper,
		};

		_returnButton.Look = MenuButtonLookHelper( UiTheme.MediumBlue );
		_leaveButton.Look = MenuButtonLookHelper( UiTheme.Magenta );
		_settingsButton.Look = MenuButtonLookHelper( UiTheme.Orange );

		Panel.AddChild( _returnButton );
		Panel.AddChild( _leaveButton );
		Panel.AddChild( _settingsButton );
	}

	protected override void OnUpdate()
	{
		EnsureTreeHelper();

		// "Escape Menu" input action (F). ESC is unreliable in-editor: Sandbox.Tools
		// grabs it to focus the editor window (ToolsDll.cs) before we see it, and TAB
		// is the panel system's keyboard-focus traversal key since 26.09.08.
		// In retail builds ESC reaches us first, so consume it there too.
		var togglePressed = Input.Pressed( "Escape Menu" );

		if ( Input.EscapePressed )
		{
			Input.EscapePressed = false;
			togglePressed = true;
		}

		if ( togglePressed )
		{
			// Never open the pause menu on top of the merged-in main menu - both are
			// ZIndex 100 full-screen modals. Suppression only blocks OPENING: if the
			// pause menu is already open, this toggle still closes it. Unwired
			// _GameManager (null) keeps the old behavior - fail-open.
			if ( !IsOpen && _GameManager is not null &&
				_GameManager.GetLocalGameState() == GameManager.LocalGameState.InMainMenu )
				return;

			// Never open the pause menu on top of the "ONE MORE ROUND?" bar menu - let
			// closing/toggling still work if the pause menu was already open first.
			if ( IsOpen || _BarMenu is null || !_BarMenu.IsOpen )
				IsOpen = !IsOpen;
		}

		Panel.SetVisible( IsOpen );
		Panel.Style.PointerEvents = IsOpen ? PointerEvents.All : PointerEvents.None;

		var size = Panel.LocalSize();
		LayoutHelper( size );

		_backdropButton.PlaceAt( new Rect( 0f, 0f, size.x, size.y ) );
		_backdropButton.SetVisible( IsOpen );

		_cardShieldButton.PlaceAt( _cardRect );

		_returnButton.PlaceAt( _returnRect );
		_leaveButton.PlaceAt( _leaveRect );
		_settingsButton.PlaceAt( _settingsRect );

		_returnButton.SetVisible( IsOpen );
		_leaveButton.SetVisible( IsOpen );
		_settingsButton.SetVisible( IsOpen );
		_cardShieldButton.SetVisible( IsOpen );
	}

	/// <summary>
	/// Computes the card, title and button rects for a root size in panel units.
	/// Called from OnUpdate to place the buttons and from the canvas draw with the same math.
	/// </summary>
	void LayoutHelper( Vector2 size )
	{
		if ( size.x <= 0f || size.y <= 0f ) return;

		var cardHeight = CardPadding * 2f + TitleBoxHeight + TitleSpacing
			+ ButtonHeight * 3f + ButtonGap * 2f;

		var left = (size.x - CardWidth) * 0.5f;
		var top = (size.y - cardHeight) * 0.5f;

		_cardRect = new Rect( left, top, CardWidth, cardHeight );
		_titleRect = new Rect( left + CardPadding, top + CardPadding,
			CardWidth - CardPadding * 2f, TitleBoxHeight );

		var buttonLeft = left + (CardWidth - ButtonWidth) * 0.5f;
		var buttonTop = top + CardPadding + TitleBoxHeight + TitleSpacing;

		_returnRect = new Rect( buttonLeft, buttonTop, ButtonWidth, ButtonHeight );
		_leaveRect = new Rect( buttonLeft, buttonTop + ButtonHeight + ButtonGap, ButtonWidth, ButtonHeight );
		_settingsRect = new Rect( buttonLeft, buttonTop + (ButtonHeight + ButtonGap) * 2f, ButtonWidth, ButtonHeight );
	}

	/// <summary>
	/// Draws the backdrop tint, the card glow, the card and the title. The buttons paint
	/// themselves on top of this.
	/// </summary>
	void DrawHelper( Painter painter )
	{
		// Painter draws in screen pixels; layout and PlaceAt work in panel units.
		var s = Panel.ScaleToScreen;
		var size = painter.Bounds.Size / s;
		LayoutHelper( size );

		using ( painter.Scope() )
		{
			// The old stylesheet blurred the scene behind the panel; a flat dark
			// translucent fill stands in for the backdrop-filter. Sized in screen
			// pixels: it covers the whole painter bounds.
			painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.75f );
			painter.Stroke = Stroke.None;
			painter.Rect( painter.Bounds );

			// Card glow: the old box-shadow, approximated with expanding soft rings.
			DrawGlowHelper( painter, _cardRect, s );

			painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.92f );
			painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, 2f * s );
			painter.Rect( _cardRect.ToScreen( s ), new Painter.CornerRadii( CardRadius * s ) );

			painter.Stroke = Stroke.None;
			painter.TextStyle = new TextStyle
			{
				FontName = UiTheme.PlainFont,
				FontSize = TitleFontSize,
				FontWeight = 900,
				Color = UiTheme.Yellow,
				Alignment = TextFlag.Center | TextFlag.DontClip,
				Shadow = new TextRendering.Shadow
				{
					Enabled = true,
					Size = 12f,
					Color = UiTheme.Yellow.WithAlpha( 0.7f ),
					Offset = Vector2.Zero,
				},
			};
			painter.Text( TitleText, _titleRect.ToScreen( s ) );
		}
	}

	/// <summary>
	/// Static rings around the card rect, standing in for the old 24px glow. Card and
	/// ring sizes are panel units; every rect is converted to screen pixels with scale.
	/// </summary>
	static void DrawGlowHelper( Painter painter, Rect card, float scale )
	{
		using ( painter.Scope() )
		{
			painter.Fill = Color.Transparent;

			for ( var ring = 0; ring < 3; ring++ )
			{
				var grow = 4f + ring * 6f;
				var alpha = 0.18f - ring * 0.06f;

				painter.Stroke = Stroke.Solid( UiTheme.MediumBlue.WithAlpha( alpha ), 8f * scale );
				painter.Rect(
					new Rect( card.Left - grow, card.Top - grow, card.Width + grow * 2f, card.Height + grow * 2f ).ToScreen( scale ),
					new Painter.CornerRadii( ( CardRadius + grow ) * scale ) );
			}
		}
	}

	/// <summary>
	/// The look of one card button: transparent with an accent border, accent label, and an
	/// accent fill with a yellow glowing label while hovered.
	/// </summary>
	static PaintButtonLook MenuButtonLookHelper( Color accent )
	{
		return new PaintButtonLook
		{
			Fill = Color.Transparent,
			HoverFill = accent,
			Border = accent,
			HoverBorder = accent,
			TextColor = accent,
			HoverTextColor = UiTheme.Yellow,
			BorderWidth = ButtonBorderWidth,
			CornerRadius = ButtonRadius,
			Font = UiTheme.PlainFont,
			FontSize = ButtonFontSize,
			CustomDraw = (button, painter) => DrawMenuButtonHelper( button, painter, accent ),
		};
	}

	/// <summary>
	/// Draws one card button: rounded accent border, accent fill while hovered, and a centered
	/// label that turns yellow with a glow while hovered (the old :hover text-shadow).
	/// </summary>
	static void DrawMenuButtonHelper( PaintButton button, Painter painter, Color accent )
	{
		// The button panel's bounds are already screen pixels; its stylesheet-derived
		// geometry (border, radius, shadow) is panel units and is scaled here with
		// ScaleToScreen. Font sizes and text shadows stay panel units: the engine scales text itself.
		var s = button.ScaleToScreen;

		using ( painter.Scope() )
		{
			var bounds = painter.Bounds;
			var hovered = button.IsHovered;

			painter.Fill = hovered ? accent : Color.Transparent;
			painter.Stroke = Stroke.Solid( accent, ButtonBorderWidth * s );
			painter.Rect( bounds, new Painter.CornerRadii( ButtonRadius * s ) );

			if ( string.IsNullOrEmpty( button.Label ) ) return;

			painter.Stroke = Stroke.None;
			painter.TextStyle = new TextStyle
			{
				FontName = UiTheme.PlainFont,
				FontSize = ButtonFontSize,
				FontWeight = 700,
				Color = hovered ? UiTheme.Yellow : accent,
				Alignment = TextFlag.Center | TextFlag.DontClip,
				Shadow = new TextRendering.Shadow
				{
					Enabled = hovered,
					Size = 10f,
					Color = UiTheme.Yellow.WithAlpha( 0.7f ),
					Offset = Vector2.Zero,
				},
			};
			painter.Text( button.Label, bounds );
		}
	}

	void CloseMenuHelper()
	{
		AudioController.Instance?.PlayUIInteract();
		IsOpen = false;
	}

	void LeaveToMainMenuHelper()
	{
		AudioController.Instance?.PlayUIInteract();
		// In editor: exits playmode. In retail: closes the game back to the platform main menu.
		Game.Close();
	}

	void OpenSettingsHelper()
	{
		AudioController.Instance?.PlayUIInteract();
		Game.Overlay.ShowSettingsModal();
	}
}
