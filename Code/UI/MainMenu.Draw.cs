// Project: sboxgamejam3
// File:    MainMenu.Draw.cs
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
// Panel tree, layout and painting for the main menu: the opaque backdrop, the title,
// the button column, the compact Top-N leaderboard column and the two modal popups
// (credits and paginated how-to-play). Layout is computed in panel units so the drawing
// and the button hit areas always agree (both work from panel units, taken from the
// values the former scss used); the drawing converts those rects to screen pixels at the
// painter boundary with Panel.ScaleToScreen, since Painter.Rect draws in raw pixels.
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
using System.Collections.Generic;
using Sandbox;

namespace Sandbox.UI;

/// <summary>
/// Building, layout and drawing for <see cref="MainMenu"/>.
/// </summary>
public partial class MainMenu
{
	// ---- menu layout (panel units, the former scss px values) ----
	const float LeaderboardWidth = 360f;
	const float LeaderboardPaddingX = 32f;
	const float LeaderboardPaddingY = 24f;
	const float LeaderboardTitleHeight = 40f;
	const float LeaderboardTitleMargin = 12f;
	const float LeaderboardRowHeight = 36f;
	const float LeaderboardRowPadX = 8f;
	const float LeaderboardRankWidth = 36f;
	const float LeaderboardScoreWidth = 80f;
	const float ColumnGap = 80f;
	const float MenuColumnWidth = 380f;
	const float TitleFontSize = 90f;
	const float TitleLineHeight = 96f;
	const float TitleGap = 40f;
	const float ButtonWidth = 380f;
	const float ButtonHeight = 72f;
	const float ButtonGap = 18f;
	const float ButtonFontSize = 26f;

	// ---- modal layout ----
	const float ModalPadding = 40f;
	const float ModalCornerRadius = 12f;
	const float ModalBorderWidth = 2f;
	const float ModalTitleHeight = 48f;
	const float ModalGap = 18f;
	const float CloseButtonWidth = 200f;
	const float CloseButtonHeight = 56f;
	const float HowToPlayWidth = 900f;
	const float HtpPageHeight = 520f;
	const float HtpImageHeight = 260f;
	const float HtpHeadingHeight = 36f;
	const float HtpHeadingMargin = 8f;
	const float HtpArrowSize = 64f;
	const float HtpCounterWidth = 80f;
	const float HtpNavGap = 40f;
	const float HtpHintHeight = 20f;
	const float HtpBodyFontSize = 22f;
	const float HtpBodyLineHeight = 30f;
	const float HtpShoutFontSize = 34f;
	const float HtpShoutHeight = 44f;
	const float HtpShoutMargin = 14f;
	const float CreditsWidth = 420f;
	const float CreditsBodyHeight = 24f;

	const float HowToPlayHeight = ModalPadding * 2f + ModalTitleHeight + HtpPageHeight
		+ HtpArrowSize + HtpHintHeight + CloseButtonHeight + ModalGap * 4f;
	const float CreditsHeight = ModalPadding * 2f + ModalTitleHeight + CreditsBodyHeight
		+ CloseButtonHeight + ModalGap * 2f;

	/// <summary>Button order in the column, matching the former markup.</summary>
	const int MenuButtonCount = 5;

	// ---- palette / fonts ----
	// The former scss redefined both $display and $body to the project's body font, so the
	// whole screen (titles, buttons, emphasis runs) is Snow Blue; only the how-to-play body
	// copy, counter and hint use the built-in Poppins.
	static readonly TextStyle TitleStyle = UiTheme.Text( UiTheme.BodyFont, TitleFontSize, UiTheme.Yellow, TextFlag.Center ) with
	{
		Shadow = new TextRendering.Shadow { Enabled = true, Size = 16f, Color = UiTheme.Yellow.WithAlpha( 0.7f ), Offset = Vector2.Zero },
	};

	static readonly TextStyle LeaderboardTitleStyle = UiTheme.Text( UiTheme.BodyFont, 32f, UiTheme.Yellow, TextFlag.CenterTop );
	static readonly TextStyle LeaderboardRowStyle = UiTheme.Text( UiTheme.BodyFont, 22f, Color.White, TextFlag.LeftCenter );
	static readonly TextStyle HtpTitleStyle = UiTheme.Text( UiTheme.BodyFont, 40f, UiTheme.Yellow, TextFlag.CenterTop );
	static readonly TextStyle HtpHeadingStyle = UiTheme.Text( UiTheme.BodyFont, 30f, UiTheme.MediumBlue, TextFlag.LeftTop );
	static readonly TextStyle HtpBodyStyle = UiTheme.Text( UiTheme.PlainFont, HtpBodyFontSize, Color.White, TextFlag.LeftTop );
	static readonly TextStyle HtpEmphasisStyle = UiTheme.Text( UiTheme.BodyFont, HtpBodyFontSize, UiTheme.Magenta, TextFlag.LeftTop );
	static readonly TextStyle HtpShoutStyle = UiTheme.Text( UiTheme.BodyFont, HtpShoutFontSize, UiTheme.Magenta, TextFlag.CenterTop );
	static readonly TextStyle HtpCounterStyle = UiTheme.Text( UiTheme.PlainFont, 24f, Color.White, TextFlag.Center );
	static readonly TextStyle HtpHintStyle = UiTheme.Text( UiTheme.PlainFont, 16f, Color.White.WithAlpha( 0.5f ), TextFlag.Center );
	static readonly TextStyle CreditsTitleStyle = UiTheme.Text( UiTheme.BodyFont, 40f, UiTheme.Yellow, TextFlag.CenterTop );
	static readonly TextStyle CreditsBodyStyle = UiTheme.Text( UiTheme.BodyFont, 20f, Color.White, TextFlag.CenterTop );

	// ---- button looks ----
	// No text-shadow on hover: the self-painting button draws a plain label.
	static readonly PaintButtonLook MenuButtonLook = new PaintButtonLook
	{
		Fill = Color.Transparent,
		HoverFill = UiTheme.MediumBlue,
		Border = UiTheme.MediumBlue,
		HoverBorder = UiTheme.MediumBlue,
		TextColor = UiTheme.MediumBlue,
		HoverTextColor = UiTheme.Yellow,
		BorderWidth = ModalBorderWidth,
		CornerRadius = 8f,
		Font = UiTheme.BodyFont,
		FontSize = ButtonFontSize,
		PressedOpacity = 1f,
	};

	static readonly PaintButtonLook PlayButtonLook = new PaintButtonLook
	{
		Fill = Color.Transparent,
		HoverFill = UiTheme.Magenta,
		Border = UiTheme.Magenta,
		HoverBorder = UiTheme.Magenta,
		TextColor = UiTheme.Magenta,
		HoverTextColor = UiTheme.Yellow,
		BorderWidth = ModalBorderWidth,
		CornerRadius = 8f,
		Font = UiTheme.BodyFont,
		FontSize = ButtonFontSize,
		PressedOpacity = 1f,
	};

	static readonly PaintButtonLook QuitButtonLook = new PaintButtonLook
	{
		Fill = Color.Transparent,
		HoverFill = UiTheme.Orange,
		Border = UiTheme.Orange,
		HoverBorder = UiTheme.Orange,
		TextColor = UiTheme.Orange,
		HoverTextColor = UiTheme.Yellow,
		BorderWidth = ModalBorderWidth,
		CornerRadius = 8f,
		Font = UiTheme.BodyFont,
		FontSize = ButtonFontSize,
		PressedOpacity = 1f,
	};

	static readonly PaintButtonLook ArrowButtonLook = new PaintButtonLook
	{
		Fill = Color.Transparent,
		HoverFill = UiTheme.MediumBlue,
		Border = UiTheme.MediumBlue,
		HoverBorder = UiTheme.MediumBlue,
		TextColor = UiTheme.MediumBlue,
		HoverTextColor = UiTheme.Yellow,
		BorderWidth = ModalBorderWidth,
		CornerRadius = 8f,
		Font = UiTheme.BodyFont,
		FontSize = 32f,
		PressedOpacity = 1f,
	};

	/// <summary>The root panel this tree was built into; it is recreated by the host.</summary>
	Panel _builtFor;

	/// <summary>Full-screen drawing surface for the backdrop, columns and modal cards.</summary>
	PaintCanvas _canvas;

	MenuButton _playButton;
	MenuButton _settingsButton;
	MenuButton _howToPlayButton;
	MenuButton _creditsButton;
	MenuButton _quitButton;
	MenuButton _prevPageButton;
	MenuButton _nextPageButton;
	MenuButton _howToPlayCloseButton;
	MenuButton _creditsCloseButton;
	OverlayCatcher _howToPlayBackdrop;
	OverlayCatcher _creditsBackdrop;
	OverlayCatcher _howToPlayCardShield;
	OverlayCatcher _creditsCardShield;

	// Layout results, refreshed every frame from the same size that the drawing uses.
	readonly Rect[] _titleRects = new Rect[2];
	readonly Rect[] _menuButtonRects = new Rect[MenuButtonCount];
	Rect _leaderboardRect;
	Rect _howToPlayCard;
	Rect _htpPageRect;
	Rect _htpImageRect;
	Rect _htpHeadingRect;
	Rect _htpTextRect;
	Rect _htpNavRect;
	Rect _htpCounterRect;
	Rect _htpHintRect;
	Rect _htpCloseRect;
	Rect _creditsCard;
	Rect _creditsBodyRect;
	Rect _creditsCloseRect;

	/// <summary>Current page art, cached so the texture is only looked up when the page flips.</summary>
	Texture _pageTexture;
	int _pageTextureIndex = -1;

	/// <summary>Scratch list of laid-out words, reused every frame.</summary>
	readonly List<Word> _words = new();

	/// <summary>
	/// A word of a how-to-play page line: its styled run and whether it glues to the word
	/// before it (the former markup ran two spans together with no space between).
	/// </summary>
	readonly struct Word
	{
		public string Text { get; init; }
		public bool Emph { get; init; }
		public bool Glued { get; init; }
	}

	/// <summary>A run of page text: body copy, or an emphasized phrase.</summary>
	readonly struct TextRun
	{
		public string Text { get; init; }
		public bool Emph { get; init; }

		/// <summary>Plain body copy.</summary>
		public static implicit operator TextRun( string text ) => new TextRun { Text = text };

		public bool StartsWithSpace => !string.IsNullOrEmpty( Text ) && Text[0] == ' ';
		public bool EndsWithSpace => !string.IsNullOrEmpty( Text ) && Text[^1] == ' ';
	}

	/// <summary>One how-to-play page: a heading, body lines and an optional shout line.</summary>
	sealed class HowToPlayPage
	{
		public string Heading { get; init; }
		public TextRun[][] Lines { get; init; }
		public string Shout { get; init; }
	}

	static TextRun Emph( string text ) => new TextRun { Text = text, Emph = true };
	static TextRun[] Line( params TextRun[] runs ) => runs;

	/// <summary>The 14 how-to-play pages: a cover page per section plus one page per line.</summary>
	static readonly HowToPlayPage[] Pages = BuildPagesHelper();

	static HowToPlayPage[] BuildPagesHelper()
	{
		return new[]
		{
			new HowToPlayPage { Heading = "How to Win" },
			new HowToPlayPage
			{
				Heading = "How to Win:",
				Lines = new[] { Line( "Run to the next pub as indicated by the arrow above your head, and ", Emph( "chug Chug CHUG!!" ) ) },
			},
			new HowToPlayPage
			{
				Heading = "How to Win:",
				Lines = new[] { Line( "Every second, you gain points based on how ", Emph( "drunk" ), " you are." ) },
			},
			new HowToPlayPage
			{
				Heading = "How to Win:",
				Lines = new[] { Line( "The more you drink, the harder the game. You move ", Emph( "faster" ), " and it's easier to ", Emph( "fall over" ), "." ) },
			},
			new HowToPlayPage
			{
				Heading = "How to Win:",
				Lines = new[] { Line( "Don't worry! The more you drink, the more ", Emph( "time" ), " gets added for you to make it to the ", Emph( "next bar" ), "!" ) },
			},
			new HowToPlayPage { Heading = "Controls" },
			new HowToPlayPage
			{
				Heading = "Controls:",
				Lines = new[] { Line( "Hold ", Emph( "Shift" ), ": Keep your pants ", Emph( "up" ), "! You silly sloppy drunk, KEEP YOUR PANTS UP!" ) },
			},
			new HowToPlayPage
			{
				Heading = "Controls:",
				Lines = new[] { Line( Emph( "A / D" ), ": Tilt left and right. Think of like you\u2019re driving a ", Emph( "unicycle." ), "." ) },
			},
			new HowToPlayPage
			{
				Heading = "Controls:",
				Lines = new[] { Line( Emph( "Forward" ), ": This is automatic, you're always going forward! You drunk you." ) },
			},
			new HowToPlayPage
			{
				Heading = "Controls:",
				Lines = new[] { Line( "Spacebar: You can ", Emph( "jump!" ) ) },
			},
			new HowToPlayPage
			{
				Heading = "Controls:",
				Lines = new[] { Line( Emph( "Shift" ), ": ... again! Drop your pants for a quick moment for ", Emph( "accurate" ), " turns." ) },
			},
			new HowToPlayPage
			{
				Heading = "End Game:",
				Lines = new[] { Line( Emph( "CASH IN~" ), ": Win ", Emph( "BIG" ), " or LOSE IT ALL. Before every drinking you get one chance to submit your ", Emph( "score." ) ) },
			},
			new HowToPlayPage { Heading = "How to Lose" },
			new HowToPlayPage
			{
				Heading = "How to Lose:",
				Lines = new[]
				{
					Line( "Don't let the clock hit zero! The higher your beer level, the easier it is to ", Emph( "knockout" ), " from simply ", Emph( "turning." ), "If your ", Emph( "timer" ), " runs out you lose ", Emph( "ALL YOUR POINTS" ), ". So every pub you hit, decide: Cash in or risk it all for ..." ),
				},
				Shout = "ANOTHER ROUND!!",
			},
		};
	}

	/// <summary>
	/// Builds the panel tree the first time (and whenever the host recreates the root panel).
	/// The canvas is added first so the backdrops and the buttons land on top of it.
	/// </summary>
	void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) )
			return;

		_builtFor = Panel;
		Panel.MakeFullscreen();

		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );

		// Modal backdrops: invisible click catchers, painted as a tint by the canvas.
		_howToPlayBackdrop = new OverlayCatcher { Clicked = CloseHowToPlay };
		Panel.AddChild( _howToPlayBackdrop );

		_creditsBackdrop = new OverlayCatcher { Clicked = CloseCredits };
		Panel.AddChild( _creditsBackdrop );

		// Card shields: absorb clicks on the modal card body so only the backdrop closes the
		// modal (the old card's pointer-events all swallowed those clicks). No Clicked handler.
		_howToPlayCardShield = new OverlayCatcher();
		Panel.AddChild( _howToPlayCardShield );

		_creditsCardShield = new OverlayCatcher();
		Panel.AddChild( _creditsCardShield );

		_playButton = new MenuButton { Label = "PLAY", Look = PlayButtonLook, Pressed = PlayPressedHelper, Clicked = PlayClicked };
		_settingsButton = new MenuButton { Label = "SETTINGS", Look = MenuButtonLook, Clicked = SettingsClicked };
		_howToPlayButton = new MenuButton { Label = "HOW TO PLAY", Look = MenuButtonLook, Clicked = HowToPlayClicked };
		_creditsButton = new MenuButton { Label = "CREDITS", Look = MenuButtonLook, Clicked = CreditsClicked };
		_quitButton = new MenuButton { Label = "QUIT", Look = QuitButtonLook, Clicked = QuitClicked };

		MenuButton[] menuButtons = { _playButton, _settingsButton, _howToPlayButton, _creditsButton, _quitButton };
		foreach ( var button in menuButtons )
			Panel.AddChild( button );

		_prevPageButton = new MenuButton { Label = "<", Look = ArrowButtonLook, Clicked = PrevPage };
		_nextPageButton = new MenuButton { Label = ">", Look = ArrowButtonLook, Clicked = NextPage };
		_howToPlayCloseButton = new MenuButton { Label = "CLOSE", Look = MenuButtonLook, Clicked = CloseHowToPlay };
		_creditsCloseButton = new MenuButton { Label = "CLOSE", Look = MenuButtonLook, Clicked = CloseCredits };

		Panel.AddChild( _prevPageButton );
		Panel.AddChild( _nextPageButton );
		Panel.AddChild( _howToPlayCloseButton );
		Panel.AddChild( _creditsCloseButton );

		// Everything starts hidden; the layout pass reveals what the current state needs.
		Panel[] panels =
		{
			_howToPlayBackdrop, _creditsBackdrop, _howToPlayCardShield, _creditsCardShield,
			_playButton, _settingsButton, _howToPlayButton,
			_creditsButton, _quitButton, _prevPageButton, _nextPageButton, _howToPlayCloseButton,
			_creditsCloseButton,
		};

		foreach ( Panel panel in panels )
			panel.SetVisible( false );

		LayoutHelper( Panel.LocalSize(), true );
	}

	/// <summary>
	/// Computes every rect for a panel of this size. Called with the live size to place the
	/// buttons and with the canvas bounds while drawing, so both agree.
	/// </summary>
	void LayoutHelper( Vector2 size, bool placeButtons )
	{
		if ( size.x <= 0f || size.y <= 0f )
			return;

		bool menuVisible = Visible && !ShowHowToPlay && !ShowCredits;

		// ---- content row: leaderboard, gap, menu column ----
		float leaderboardHeight = LeaderboardPaddingY * 2f + LeaderboardTitleHeight + LeaderboardTitleMargin
			+ LeaderboardRowCountHelper() * LeaderboardRowHeight;
		float buttonsHeight = MenuButtonCount * ButtonHeight + ( MenuButtonCount - 1 ) * ButtonGap;
		float menuHeight = TitleLineHeight * 2f + TitleGap + buttonsHeight;
		float contentHeight = MathF.Max( menuHeight, leaderboardHeight );
		float contentWidth = LeaderboardWidth + ColumnGap + MenuColumnWidth;
		float contentLeft = ( size.x - contentWidth ) * 0.5f;
		float contentTop = ( size.y - contentHeight ) * 0.5f;

		_leaderboardRect = new Rect( contentLeft,
			contentTop + ( contentHeight - leaderboardHeight ) * 0.5f,
			LeaderboardWidth, leaderboardHeight );

		float menuLeft = contentLeft + LeaderboardWidth + ColumnGap;
		float menuTop = contentTop + ( contentHeight - menuHeight ) * 0.5f;
		_titleRects[0] = new Rect( menuLeft, menuTop, MenuColumnWidth, TitleLineHeight );
		_titleRects[1] = new Rect( menuLeft, menuTop + TitleLineHeight, MenuColumnWidth, TitleLineHeight );

		float buttonsTop = menuTop + TitleLineHeight * 2f + TitleGap;
		for ( int i = 0; i < MenuButtonCount; i++ )
			_menuButtonRects[i] = new Rect( menuLeft, buttonsTop + i * ( ButtonHeight + ButtonGap ), ButtonWidth, ButtonHeight );

		// ---- how-to-play card, centered ----
		_howToPlayCard = new Rect( ( size.x - HowToPlayWidth ) * 0.5f, ( size.y - HowToPlayHeight ) * 0.5f,
			HowToPlayWidth, HowToPlayHeight );

		float pageLeft = _howToPlayCard.Left + ModalPadding;
		float pageTop = _howToPlayCard.Top + ModalPadding + ModalTitleHeight + ModalGap;
		float pageWidth = HowToPlayWidth - ModalPadding * 2f;
		_htpPageRect = new Rect( pageLeft, pageTop, pageWidth, HtpPageHeight );
		_htpImageRect = new Rect( pageLeft, pageTop, pageWidth, HtpImageHeight );
		_htpHeadingRect = new Rect( pageLeft, pageTop + HtpImageHeight + 10f, pageWidth, HtpHeadingHeight );
		float textTop = pageTop + HtpImageHeight + 10f + HtpHeadingHeight + HtpHeadingMargin;
		_htpTextRect = new Rect( pageLeft, textTop, pageWidth, pageTop + HtpPageHeight - textTop );

		float navTop = pageTop + HtpPageHeight + ModalGap;
		float navWidth = HtpArrowSize + HtpNavGap + HtpCounterWidth + HtpNavGap + HtpArrowSize;
		float navLeft = _howToPlayCard.Left + ModalPadding + ( pageWidth - navWidth ) * 0.5f;
		_htpNavRect = new Rect( navLeft, navTop, navWidth, HtpArrowSize );
		_htpCounterRect = new Rect( navLeft + HtpArrowSize + HtpNavGap, navTop, HtpCounterWidth, HtpArrowSize );
		_htpHintRect = new Rect( _howToPlayCard.Left + ModalPadding, navTop + HtpArrowSize + ModalGap,
			pageWidth, HtpHintHeight );
		_htpCloseRect = new Rect( _howToPlayCard.Left + ( HowToPlayWidth - CloseButtonWidth ) * 0.5f,
			_htpHintRect.Bottom + ModalGap, CloseButtonWidth, CloseButtonHeight );

		// ---- credits card, centered ----
		_creditsCard = new Rect( ( size.x - CreditsWidth ) * 0.5f, ( size.y - CreditsHeight ) * 0.5f,
			CreditsWidth, CreditsHeight );
		_creditsBodyRect = new Rect( _creditsCard.Left + ModalPadding,
			_creditsCard.Top + ModalPadding + ModalTitleHeight + ModalGap,
			CreditsWidth - ModalPadding * 2f, CreditsBodyHeight );
		_creditsCloseRect = new Rect( _creditsCard.Left + ( CreditsWidth - CloseButtonWidth ) * 0.5f,
			_creditsBodyRect.Bottom + ModalGap, CloseButtonWidth, CloseButtonHeight );

		if ( !placeButtons )
			return;

		_playButton.Label = IsSearchingForLobby ? "SEARCHING..." : "PLAY";
		PlaceButtonHelper( _playButton, _menuButtonRects[0], menuVisible );
		PlaceButtonHelper( _settingsButton, _menuButtonRects[1], menuVisible );
		PlaceButtonHelper( _howToPlayButton, _menuButtonRects[2], menuVisible );
		PlaceButtonHelper( _creditsButton, _menuButtonRects[3], menuVisible );
		PlaceButtonHelper( _quitButton, _menuButtonRects[4], menuVisible );

		bool howToPlay = Visible && ShowHowToPlay;
		PlaceButtonHelper( _prevPageButton, new Rect( _htpNavRect.Left, _htpNavRect.Top, HtpArrowSize, HtpArrowSize ), howToPlay );
		PlaceButtonHelper( _nextPageButton, new Rect( _htpNavRect.Right - HtpArrowSize, _htpNavRect.Top, HtpArrowSize, HtpArrowSize ), howToPlay );
		PlaceButtonHelper( _howToPlayCloseButton, _htpCloseRect, howToPlay );

		bool credits = Visible && ShowCredits;
		PlaceButtonHelper( _creditsCloseButton, _creditsCloseRect, credits );

		// The backdrops cover the whole screen but only while their modal is up.
		SetFullScreenHelper( _howToPlayBackdrop, howToPlay, size );
		SetFullScreenHelper( _creditsBackdrop, credits, size );

		// The card shields sit exactly over their modal's card and absorb clicks on it.
		SetCardShieldHelper( _howToPlayCardShield, howToPlay, _howToPlayCard );
		SetCardShieldHelper( _creditsCardShield, credits, _creditsCard );
	}

	/// <summary>Shows or hides a card click shield and, while shown, keeps it over its modal card.</summary>
	static void SetCardShieldHelper( Panel panel, bool visible, Rect rect )
	{
		panel.SetVisible( visible );

		if ( visible )
			panel.PlaceAt( rect );
	}

	/// <summary>Shows or hides a button and, while shown, keeps its rect up to date.</summary>
	static void PlaceButtonHelper( MenuButton button, Rect rect, bool visible )
	{
		button.SetVisible( visible );

		if ( visible )
			button.PlaceAt( rect );
	}

	/// <summary>Shows or hides a full-screen click catcher.</summary>
	static void SetFullScreenHelper( Panel panel, bool visible, Vector2 size )
	{
		panel.SetVisible( visible );

		if ( visible )
			panel.PlaceAt( new Rect( 0f, 0f, size.x, size.y ) );
	}

	/// <summary>Paints the whole screen: backdrop, both columns, then the modal layers.</summary>
	void DrawHelper( Painter painter )
	{
		if ( !Visible )
			return;

		// Painter draws in screen pixels, so layout runs in panel units and the rects are
		// converted back to pixels as they are drawn.
		float s = Panel.ScaleToScreen;
		Rect bounds = painter.Bounds;
		LayoutHelper( bounds.Size * Panel.ScaleFromScreen, false );

		using ( painter.Scope() )
		{
			// The title screen is drawn over the live scene with no backdrop.
			DrawLeaderboardHelper( painter, s );
			DrawMenuColumnHelper( painter, s );

			if ( ShowHowToPlay )
				DrawHowToPlayHelper( painter, bounds, s );

			if ( ShowCredits )
				DrawCreditsHelper( painter, bounds, s );
		}
	}

	void DrawLeaderboardHelper( Painter painter, float s )
	{
		painter.Fill = UiTheme.MediumBlue.WithAlpha( 0.08f );
		painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, ModalBorderWidth * s );
		painter.Rect( _leaderboardRect.ToScreen( s ), new Painter.CornerRadii( ModalCornerRadius * s ) );
		painter.Stroke = Stroke.None;

		painter.TextStyle = LeaderboardTitleStyle;
		painter.Text( "BEST DRUNKARDS", new Rect(
			_leaderboardRect.Left + LeaderboardPaddingX,
			_leaderboardRect.Top + LeaderboardPaddingY,
			LeaderboardWidth - LeaderboardPaddingX * 2f,
			LeaderboardTitleHeight ).ToScreen( s ) );

		List<Row> rows = TopRows;
		float rowLeft = _leaderboardRect.Left + LeaderboardPaddingX + LeaderboardRowPadX;
		float rowWidth = LeaderboardWidth - ( LeaderboardPaddingX + LeaderboardRowPadX ) * 2f;
		float rowTop = _leaderboardRect.Top + LeaderboardPaddingY + LeaderboardTitleHeight + LeaderboardTitleMargin;

		for ( int i = 0; i < rows.Count; i++ )
		{
			Row row = rows[i];
			var style = LeaderboardRowStyle with { Color = RowColorHelper( row ) };

			if ( row.IsLocal )
			{
				var shadowStyle = style with
				{
					Shadow = new TextRendering.Shadow
					{
						Enabled = true,
						Size = 10f,
						Color = UiTheme.Magenta.WithAlpha( 0.7f ),
						Offset = Vector2.Zero,
					},
				};

				// Rank, name and score share one vertical center so the row lines up with the
				// unshadowed rows above it.
				float centerY = rowTop + LeaderboardRowHeight * 0.5f;
				DrawShadowedCellHelper( painter, $"{row.Rank}.", style, shadowStyle, rowLeft, false, centerY, s );
				DrawShadowedCellHelper( painter, row.Entry.DisplayName, style, shadowStyle, rowLeft + LeaderboardRankWidth, false, centerY, s );
				DrawShadowedCellHelper( painter, ( (int)row.Entry.Value ).ToString( "D4" ), style, shadowStyle, rowLeft + rowWidth, true, centerY, s );
			}
			else
			{
				painter.TextStyle = style with { Alignment = TextFlag.LeftCenter | TextFlag.DontClip };
				painter.Text( $"{row.Rank}.", new Rect( rowLeft, rowTop, LeaderboardRankWidth, LeaderboardRowHeight ).ToScreen( s ) );
				painter.Text( row.Entry.DisplayName, new Rect( rowLeft + LeaderboardRankWidth, rowTop,
					rowWidth - LeaderboardRankWidth - LeaderboardScoreWidth, LeaderboardRowHeight ).ToScreen( s ) );

				painter.TextStyle = style with { Alignment = TextFlag.RightCenter | TextFlag.DontClip };
				painter.Text( ( (int)row.Entry.Value ).ToString( "D4" ), new Rect( rowLeft + rowWidth - LeaderboardScoreWidth,
					rowTop, LeaderboardScoreWidth, LeaderboardRowHeight ).ToScreen( s ) );
			}

			rowTop += LeaderboardRowHeight;
		}
	}

	/// <summary>
	/// Draws one leaderboard cell whose style carries a shadow. A shadow grows the laid-out block
	/// by Shadow.Size * 3 on every side and Painter aligns that whole block, so a left- or
	/// right-aligned shadowed cell lands shifted. Measure the glyphs with the same style minus the
	/// shadow, put a box of exactly that size where the glyphs belong - left edge at
	/// <paramref name="anchorX"/> for a left cell, right edge there for a right one - and draw the
	/// shadowed block centered in it, so the shadow's own margins cancel around the glyphs.
	/// </summary>
	void DrawShadowedCellHelper( Painter painter, string text, TextStyle plainStyle, TextStyle shadowStyle, float anchorX, bool anchorRight, float centerY, float s )
	{
		painter.TextStyle = plainStyle;
		var glyphs = painter.MeasureText( text );

		// MeasureText returns screen pixels (it includes Panel.ScaleToScreen), so the panel-unit
		// anchor and centre are converted here to keep the whole cell in one unit.
		anchorX *= s;
		centerY *= s;
		float left = anchorRight ? anchorX - glyphs.x : anchorX;
		painter.TextStyle = shadowStyle with { Alignment = TextFlag.Center | TextFlag.DontClip };
		painter.Text( text, new Rect( left, centerY - glyphs.y * 0.5f, glyphs.x, glyphs.y ) );
	}

	/// <summary>How many rows the leaderboard column lays out - the same count TopRows builds.</summary>
	int LeaderboardRowCountHelper() => Math.Min( _DisplayTopCount, Leaderboard.Count );

	static Color RowColorHelper( Row row )
	{
		if ( row.IsLocal )
			return UiTheme.Magenta;

		return row.Rank switch
		{
			1 => UiTheme.Yellow,
			2 => UiTheme.MediumBlue,
			3 => UiTheme.Orange,
			_ => Color.White,
		};
	}

	void DrawMenuColumnHelper( Painter painter, float s )
	{
		painter.TextStyle = TitleStyle;
		painter.Text( "Bottoms Up:", _titleRects[0].ToScreen( s ) );
		painter.Text( "Speedrun Debauchery", _titleRects[1].ToScreen( s ) );
	}

	void DrawHowToPlayHelper( Painter painter, Rect bounds, float s )
	{
		DrawScrimHelper( painter, bounds );
		DrawCardHelper( painter, _howToPlayCard, s );

		painter.TextStyle = HtpTitleStyle;
		painter.Text( "HOW TO PLAY", new Rect( _howToPlayCard.Left + ModalPadding, _howToPlayCard.Top + ModalPadding,
			HowToPlayWidth - ModalPadding * 2f, ModalTitleHeight ).ToScreen( s ) );

		DrawPageImageHelper( painter, s );
		DrawPageTextHelper( painter, s );

		painter.TextStyle = HtpCounterStyle;
		painter.Text( $"{CurrentPage + 1} / {PageCount}", _htpCounterRect.ToScreen( s ) );

		painter.TextStyle = HtpHintStyle;
		painter.Text( "A / D or LEFT / RIGHT ARROWS to flip pages", _htpHintRect.ToScreen( s ) );
	}

	void DrawCreditsHelper( Painter painter, Rect bounds, float s )
	{
		DrawScrimHelper( painter, bounds );
		DrawCardHelper( painter, _creditsCard, s );

		painter.TextStyle = CreditsTitleStyle;
		painter.Text( "CREDITS", new Rect( _creditsCard.Left + ModalPadding, _creditsCard.Top + ModalPadding,
			CreditsWidth - ModalPadding * 2f, ModalTitleHeight ).ToScreen( s ) );

		painter.TextStyle = CreditsBodyStyle;
		painter.Text( "Glasses models: CGTrader / mdaniel112", _creditsBodyRect.ToScreen( s ) );
	}

	/// <summary>
	/// The translucent darkened backdrop the two modals share. The scss also blurred what was
	/// behind it; that is not reproduced.
	/// </summary>
	static void DrawScrimHelper( Painter painter, Rect bounds )
	{
		painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.85f );
		painter.Stroke = Stroke.None;
		painter.Rect( bounds );
	}

	/// <summary>Rounded card with a border and a soft outer glow (the scss box-shadow).</summary>
	static void DrawCardHelper( Painter painter, Rect rect, float s )
	{
		Rect screen = rect.ToScreen( s );
		float radius = ModalCornerRadius * s;

		for ( int i = 0; i < 3; i++ )
		{
			float width = ( 24f - i * 8f ) * s;
			painter.Fill = Color.Transparent;
			painter.Stroke = Stroke.Solid( UiTheme.MediumBlue.WithAlpha( 0.08f ), width );
			painter.Rect( screen, new Painter.CornerRadii( radius ) );
		}

		painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.95f );
		painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, ModalBorderWidth * s );
		painter.Rect( screen, new Painter.CornerRadii( radius ) );
		painter.Stroke = Stroke.None;
	}

	/// <summary>Draws the current page's art, aspect-fitted into the image slot. Missing art is skipped.</summary>
	void DrawPageImageHelper( Painter painter, float s )
	{
		Texture texture = PageTextureHelper();

		if ( texture is null )
			return;

		painter.Texture( texture, FitHelper( texture.Size, _htpImageRect.ToScreen( s ) ) );
	}

	/// <summary>
	/// Loads (once per page) the per-page art, named by zero-based page index, relative to Assets.
	/// </summary>
	Texture PageTextureHelper()
	{
		if ( _pageTextureIndex == CurrentPage )
			return _pageTexture;

		_pageTextureIndex = CurrentPage;
		_pageTexture = Texture.Load( $"materials/howto/page{CurrentPage}.png", false );

		return _pageTexture;
	}

	/// <summary>The largest rect with this content's aspect ratio that fits inside bounds.</summary>
	static Rect FitHelper( Vector2 content, Rect bounds )
	{
		if ( content.x <= 0f || content.y <= 0f )
			return bounds;

		float scale = MathF.Min( bounds.Width / content.x, bounds.Height / content.y );
		float width = content.x * scale;
		float height = content.y * scale;

		return new Rect( bounds.Left + ( bounds.Width - width ) * 0.5f,
			bounds.Top + ( bounds.Height - height ) * 0.5f, width, height );
	}

	void DrawPageTextHelper( Painter painter, float s )
	{
		if ( CurrentPage < 0 || CurrentPage >= Pages.Length )
			return;

		HowToPlayPage page = Pages[CurrentPage];

		painter.TextStyle = HtpHeadingStyle;
		painter.Text( page.Heading, _htpHeadingRect.ToScreen( s ) );

		float top = _htpTextRect.Top;

		if ( page.Lines is not null )
		{
			foreach ( TextRun[] line in page.Lines )
			{
				// DrawRunsHelper word-wraps one entry onto several painter lines; the next
				// entry (and the page Shout) must clear every line it actually drew.
				int lines = DrawRunsHelper( painter, line, top, s );
				top += lines * HtpBodyLineHeight;
			}
		}

		if ( string.IsNullOrEmpty( page.Shout ) )
			return;

		painter.TextStyle = HtpShoutStyle;
		painter.Text( page.Shout, new Rect( _htpTextRect.Left, top + HtpShoutMargin, _htpTextRect.Width, HtpShoutHeight ).ToScreen( s ) );
	}

	/// <summary>
	/// Draws one page line: its runs are laid out word by word, each word keeping its own
	/// style, so the emphasized phrases stay inline with the body copy across wrapped lines.
	/// Returns the number of painter lines the entry occupied (space for <paramref name="top"/>
	/// to advance by), so a wrapped entry does not get overdrawn by the next one.
	/// </summary>
	int DrawRunsHelper( Painter painter, TextRun[] runs, float top, float s )
	{
		BuildWordsHelper( runs );

		// MeasureText includes Panel.ScaleToScreen, so this word wrap works in pixels: the line
		// rect is converted once and everything downstream (line height, width fudge) follows.
		Rect lineRect = _htpTextRect.ToScreen( s );
		float left = lineRect.Left;
		float right = lineRect.Right;
		float lineHeight = HtpBodyLineHeight * s;

		painter.TextStyle = HtpBodyStyle;
		float spaceWidth = painter.MeasureText( " " ).x;

		float x = left;
		float y = top * s;
		bool lineStart = true;
		int lines = 1;

		foreach ( Word word in _words )
		{
			painter.TextStyle = word.Emph ? HtpEmphasisStyle : HtpBodyStyle;
			float width = painter.MeasureText( word.Text ).x;

			if ( !lineStart )
			{
				float advance = word.Glued ? 0f : spaceWidth;

				if ( x + advance + width > right )
				{
					x = left;
					y += lineHeight;
					lines++;
					lineStart = true;
				}
				else
				{
					x += advance;
				}
			}

			painter.Text( word.Text, new Rect( x, y, width + 2f * s, lineHeight ) );
			x += width;
			lineStart = false;
		}

		return lines;
	}

	/// <summary>Splits a line's runs into words, keeping each word's style and glue flag.</summary>
	void BuildWordsHelper( TextRun[] runs )
	{
		_words.Clear();

		for ( int r = 0; r < runs.Length; r++ )
		{
			bool glueRun = r > 0 && !runs[r].StartsWithSpace && !runs[r - 1].EndsWithSpace;
			string[] parts = runs[r].Text.Split( ' ' );

			for ( int w = 0; w < parts.Length; w++ )
			{
				if ( parts[w].Length == 0 )
					continue;

				_words.Add( new Word { Text = parts[w], Emph = runs[r].Emph, Glued = glueRun && w == 0 } );
			}
		}
	}
}
