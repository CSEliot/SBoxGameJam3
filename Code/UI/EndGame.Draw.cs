// Project: sboxgamejam3
// File:    EndGame.Draw.cs
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
// Drawing half of the code-built End-Game screen: builds the panel tree when the
// component's root Panel appears (or is recreated), computes the two-column
// layout from a panel size, and paints the backdrop, title, leaderboard, score,
// stats and WASTED stamp with Painter.

using System;

namespace Sandbox.UI;

public partial class EndGame
{
	// Layout constants in panel units: the old scss px values kept as panel units (a 1080-tall
	// reference). Painter geometry is screen pixels, so every drawn rect and figure is multiplied
	// by Panel.ScaleToScreen at the painter call; text and text shadows are already scaled by the
	// engine, so TextStyle values stay in panel units. LayoutHelper only ever sees panel units.
	const float TitleSize = 100f;
	const float TitleMarginBottom = 24f;

	const float LeaderboardWidth = 400f;
	const float LeaderboardRadius = 12f;
	const float LeaderboardBorderWidth = 2f;
	const float LeaderboardPaddingX = 32f;
	const float LeaderboardPaddingY = 24f;
	const float LeaderboardTitleSize = 40f;
	const float LeaderboardTitleMarginBottom = 12f;
	const float DividerHeight = 2f;
	const float DividerMarginY = 8f;

	const float RowTextSize = 24f;
	const float RowPaddingY = 4f;
	const float RowPaddingX = 8f;
	const float RowRankMinWidth = 40f;
	const float RowScoreMinWidth = 90f;

	const float YourScoreSize = 70f;
	const float YourScoreWrapMarginBottom = 16f;
	const float StatSize = 40f;
	const float StatGap = 8f;
	const float StatsMarginBottom = 32f;

	const float ButtonWidth = 400f;
	const float ButtonHeight = 70f;
	const float ButtonGap = 20f;
	const float ButtonRadius = 8f;
	const float ButtonTextSize = 26f;

	const float BodyGap = 48f;

	// Rects computed by LayoutHelper from a panel size, shared by OnUpdate (button
	// placement) and DrawHelper (painting).
	Rect _titleRect;
	Rect _leaderboardRect;
	Rect _yourScoreRect;
	Rect _statsRect;
	Rect _tryAgainRect;
	Rect _quitRect;

	// The centre column is content-sized (the old scss flex column had no width), so its width
	// is the widest centre caption measured each draw; the buttons keep their fixed width and
	// stay centred inside it.
	float _centerColumnWidth = ButtonWidth;

	// The leaderboard title's line height: the stylesheet's 1.2x size, or the taller measured
	// glyph extent, so the title cannot spill onto the first row.
	float _leaderboardTitleHeight = LeaderboardTitleSize * 1.2f;

	/// <summary>
	/// Builds the panel tree when the component's root Panel appears or is recreated
	/// (PanelComponent destroys and re-creates its root), so the canvas and the buttons
	/// never point at a stale tree.
	/// </summary>
	void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) )
			return;

		_builtFor = Panel;
		Panel.MakeFullscreen();

		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );

		// Buttons after the canvas so they sit on top of it.
		_tryAgainButton = new PaintButton
		{
			Label = "TRY AGAIN",
			Clicked = TryAgainClicked,
			Look = new PaintButtonLook { CustomDraw = DrawTryAgainHelper },
		};
		Panel.AddChild( _tryAgainButton );

		_quitButton = new PaintButton
		{
			Label = "QUIT",
			Clicked = QuitClicked,
			Look = new PaintButtonLook { CustomDraw = DrawQuitHelper },
		};
		Panel.AddChild( _quitButton );
	}

	/// <summary>
	/// The two-column layout: leaderboard on the left, title above and the score / stats /
	/// buttons stacked in a centered column on the right. Everything is centered as a whole
	/// block, mirroring the old fullscreen flex layout.
	/// </summary>
	void LayoutHelper( Vector2 size )
	{
		var rows = TopRows;
		var pinned = LocalPinnedRow;

		float rowHeight = RowTextSize * 1.2f + RowPaddingY * 2f;
		float lbTitleHeight = _leaderboardTitleHeight;
		float dividerBlock = DividerHeight + DividerMarginY * 2f;

		float leaderboardContent = lbTitleHeight + LeaderboardTitleMarginBottom + rows.Count * rowHeight;
		if ( pinned.HasValue )
			leaderboardContent += dividerBlock + rowHeight;

		float leaderboardHeight = leaderboardContent + LeaderboardPaddingY * 2f;

		float yourScoreHeight = YourScoreSize * 1.2f;
		float statsHeight = StatSize * 1.2f * 2f + StatGap;
		float buttonsHeight = ButtonHeight * 2f + ButtonGap;
		float centerHeight = yourScoreHeight + YourScoreWrapMarginBottom + statsHeight + StatsMarginBottom + buttonsHeight;
		// Content-sized centre column: widest caption, never narrower than the buttons.
		float centerWidth = _centerColumnWidth;

		float bodyWidth = LeaderboardWidth + BodyGap + centerWidth;
		float bodyHeight = MathF.Max( leaderboardHeight, centerHeight );

		float titleHeight = TitleSize * 1.2f;
		float totalHeight = titleHeight + TitleMarginBottom + bodyHeight;

		float top = MathF.Max( 0f, ( size.y - totalHeight ) * 0.5f );
		float left = MathF.Max( 0f, ( size.x - bodyWidth ) * 0.5f );

		_titleRect = new Rect( 0f, top, size.x, titleHeight );

		float bodyTop = top + titleHeight + TitleMarginBottom;
		_leaderboardRect = new Rect( left, bodyTop, LeaderboardWidth, leaderboardHeight );

		float centerLeft = left + LeaderboardWidth + BodyGap;
		// The old scss .body row is align-items: flex-start, so the centre column is top-aligned
		// with the leaderboard rather than centred vertically in the body.
		float centerTop = bodyTop;

		_yourScoreRect = new Rect( centerLeft, centerTop, centerWidth, yourScoreHeight );
		_statsRect = new Rect( centerLeft, centerTop + yourScoreHeight + YourScoreWrapMarginBottom, centerWidth, statsHeight );

		float buttonsTop = centerTop + yourScoreHeight + YourScoreWrapMarginBottom + statsHeight + StatsMarginBottom;
		// The 400-wide buttons stay centred inside the (possibly wider) content-sized column.
		float buttonLeft = centerLeft + ( centerWidth - ButtonWidth ) * 0.5f;
		_tryAgainRect = new Rect( buttonLeft, buttonsTop, ButtonWidth, ButtonHeight );
		_quitRect = new Rect( buttonLeft, buttonsTop + ButtonHeight + ButtonGap, ButtonWidth, ButtonHeight );
	}

	/// <summary>
	/// Sizes the centre column to its content: the widest of the score and stat captions, with
	/// the buttons' fixed width as the floor. The old scss column was a flex column with no
	/// width, so a fixed 400-unit column let the 70px score text overflow into the leaderboard.
	/// MeasureText returns screen pixels (it already carries ScaleToScreen), so divide by s.
	/// </summary>
	void MeasureCenterColumnHelper( Painter painter, float s )
	{
		float width = ButtonWidth;

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, YourScoreSize, UiTheme.Yellow, TextFlag.Center );
		width = MathF.Max( width, painter.MeasureText( $"YOUR SCORE: {ScoreText}" ).x / s );

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, StatSize, UiTheme.Magenta, TextFlag.Center );
		width = MathF.Max( width, painter.MeasureText( $"Beers Drunk: {BeersText}" ).x / s );
		width = MathF.Max( width, painter.MeasureText( $"Bars Visited: {BarsVisited}" ).x / s );

		_centerColumnWidth = width;

		// The leaderboard title's line height: the stylesheet's 1.2x size, or the taller measured
		// glyph extent, whichever is taller (the display font's glyphs outrun the 1.2x box and
		// spilled onto row 1). Measured in this pre-layout pass so LayoutHelper and the row
		// drawing in one DrawHelper call use the same value.
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, LeaderboardTitleSize, UiTheme.Yellow, TextFlag.Center );
		_leaderboardTitleHeight = MathF.Max( LeaderboardTitleSize * 1.2f, painter.MeasureText( "BEST DRUNKARDS" ).y / s );
	}

	/// <summary>
	/// Everything on this screen, painted into the fullscreen canvas.
	/// </summary>
	void DrawHelper( Painter painter )
	{
		float s = Panel.ScaleToScreen;
		var size = painter.Bounds.Size;
		if ( size.x <= 0f || size.y <= 0f )
			return;

		// The painter works in screen pixels; the layout stays in panel units. The centre
		// column is content-sized, so measure its widest text before laying it out.
		MeasureCenterColumnHelper( painter, s );
		LayoutHelper( size / s );

		// Backdrop: the old rgba( #00166D, 0.92 ) plus a 6px blur, which the painter has no
		// cheap equivalent for - approximated with the flat tint.
		painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.92f );
		painter.Stroke = Stroke.None;
		painter.Rect( new Rect( 0f, 0f, size.x, size.y ) );

		DrawTitleHelper( painter );
		DrawLeaderboardHelper( painter );
		DrawScoreHelper( painter );
		DrawStatsHelper( painter );
	}

	/// <summary>The big display-font title: yellow on a cash-in, orange on a game over.</summary>
	void DrawTitleHelper( Painter painter )
	{
		bool cashedIn = _progress?.EndedByCashOut ?? false;
		var color = cashedIn ? UiTheme.Yellow : UiTheme.Orange;

		painter.Stroke = Stroke.None;
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, TitleSize, color, TextFlag.Center )
			.WithShadow( color.WithAlpha( 0.7f ), 16f, Vector2.Zero );
		painter.Text( TitleText, _titleRect.ToScreen( Panel.ScaleToScreen ) );
	}

	/// <summary>The bordered leaderboard card: title, Top N rows, divider and the pinned local row.</summary>
	void DrawLeaderboardHelper( Painter painter )
	{
		float s = Panel.ScaleToScreen;
		var box = _leaderboardRect;

		painter.Fill = UiTheme.DeepBlue.WithAlpha( 0.6f );
		painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, LeaderboardBorderWidth * s );
		painter.Rect( box.ToScreen( s ), new Painter.CornerRadii( LeaderboardRadius * s ) );

		float contentLeft = box.Left + LeaderboardPaddingX;
		float contentWidth = box.Width - LeaderboardPaddingX * 2f;
		float y = box.Top + LeaderboardPaddingY;

		painter.Stroke = Stroke.None;
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, LeaderboardTitleSize, UiTheme.Yellow, TextFlag.Center );
		// _leaderboardTitleHeight was measured in the pre-layout pass (MeasureCenterColumnHelper).
		painter.Text( "BEST DRUNKARDS", new Rect( contentLeft, y, contentWidth, _leaderboardTitleHeight ).ToScreen( s ) );
		y += _leaderboardTitleHeight + LeaderboardTitleMarginBottom;

		float rowHeight = RowTextSize * 1.2f + RowPaddingY * 2f;

		foreach ( var row in TopRows )
		{
			DrawRowHelper( painter, row, contentLeft, contentWidth, y, rowHeight );
			y += rowHeight;
		}

		var pinned = LocalPinnedRow;
		if ( pinned.HasValue )
		{
			painter.Fill = UiTheme.MediumBlue;
			painter.Rect( new Rect( contentLeft, y + DividerMarginY, contentWidth, DividerHeight ).ToScreen( s ) );
			y += DividerHeight + DividerMarginY * 2f;

			DrawRowHelper( painter, pinned.Value, contentLeft, contentWidth, y, rowHeight );
		}
	}

	/// <summary>One leaderboard row: 1-based rank, name, 4-digit score. Rank colors, local magenta.</summary>
	void DrawRowHelper( Painter painter, Row row, float contentLeft, float contentWidth, float y, float rowHeight )
	{
		Color color = Color.White;
		if ( row.Rank == 1 ) color = UiTheme.Yellow;
		else if ( row.Rank == 2 ) color = UiTheme.MediumBlue;
		else if ( row.Rank == 3 ) color = UiTheme.Orange;

		bool local = row.IsLocal;
		if ( local ) color = UiTheme.Magenta;

		float s = Panel.ScaleToScreen;

		painter.Stroke = Stroke.None;

		float textLeft = contentLeft + RowPaddingX;
		float textWidth = contentWidth - RowPaddingX * 2f;
		float scoreLeft = textLeft + textWidth - RowScoreMinWidth;
		float nameLeft = textLeft + RowRankMinWidth;
		float nameWidth = MathF.Max( 0f, scoreLeft - nameLeft );

		// The row's text column, shared by every cell so the alignments agree.
		float rowTop = y + RowPaddingY;
		float centerY = rowTop + rowHeight * 0.5f;

		var plainStyle = UiTheme.Text( UiTheme.BodyFont, RowTextSize, color );

		// Fit the name to its column (card inner width minus the rank and score columns):
		// a name wider than that would run into the score, so cut it with an ASCII ellipsis.
		string name = FitNameHelper( painter, row.DisplayName, plainStyle, nameWidth * s );

		if ( local )
		{
			var shadowStyle = plainStyle.WithShadow( UiTheme.Magenta.WithAlpha( 0.7f ), 10f, Vector2.Zero );

			DrawShadowedCellHelper( painter, $"{row.Rank}.", plainStyle, shadowStyle, textLeft, false, centerY );
			DrawShadowedCellHelper( painter, name, plainStyle, shadowStyle, nameLeft, false, centerY );
			DrawShadowedCellHelper( painter, ( (int)row.Value ).ToString( "D4" ), plainStyle, shadowStyle, scoreLeft + RowScoreMinWidth, true, centerY );
			return;
		}

		// The rank rides the name's vertical alignment so the row reads as one line.
		painter.TextStyle = plainStyle with { Alignment = TextFlag.LeftCenter | TextFlag.DontClip };
		painter.Text( $"{row.Rank}.", new Rect( new Vector2( textLeft, rowTop ), new Vector2( RowRankMinWidth, rowHeight ) ).ToScreen( s ) );
		painter.Text( name, new Rect( new Vector2( nameLeft, rowTop ), new Vector2( nameWidth, rowHeight ) ).ToScreen( s ) );

		painter.TextStyle = plainStyle with { Alignment = TextFlag.RightCenter | TextFlag.DontClip };
		painter.Text( ( (int)row.Value ).ToString( "D4" ), new Rect( new Vector2( scoreLeft, rowTop ), new Vector2( RowScoreMinWidth, rowHeight ) ).ToScreen( s ) );
	}

	/// <summary>
	/// Trims <paramref name="text"/> so it fits <paramref name="maxWidthPx"/> screen pixels,
	/// appending an ASCII "..." when it is cut. Measured with the same style it will be drawn
	/// with, so the returned string never runs past the column (and never into the score).
	/// </summary>
	string FitNameHelper( Painter painter, string text, TextStyle style, float maxWidthPx )
	{
		if ( string.IsNullOrEmpty( text ) || maxWidthPx <= 0f )
			return text;

		painter.TextStyle = style;
		if ( painter.MeasureText( text ).x <= maxWidthPx )
			return text;

		const string ellipsis = "...";
		if ( painter.MeasureText( ellipsis ).x > maxWidthPx )
			return string.Empty;

		for ( int length = text.Length - 1; length > 0; length-- )
		{
			string candidate = text.Substring( 0, length ) + ellipsis;
			if ( painter.MeasureText( candidate ).x <= maxWidthPx )
				return candidate;
		}

		return ellipsis;
	}

	/// <summary>
	/// Draws one row cell whose style carries a shadow. A shadow grows the laid-out block by
	/// Shadow.Size * 3 on every side and Painter aligns that whole block, so a left- or
	/// right-aligned shadowed cell lands shifted. Measure the glyphs with the same style minus
	/// the shadow, put a box of exactly that size where the glyphs belong - left edge at
	/// <paramref name="anchorX"/> for a left cell, right edge there for a right one - and draw the
	/// shadowed block centered in it, so the shadow's own margins cancel around the glyphs.
	/// </summary>
	void DrawShadowedCellHelper( Painter painter, string text, TextStyle plainStyle, TextStyle shadowStyle, float anchorX, bool anchorRight, float centerY )
	{
		// MeasureText returns screen pixels, so the centring is done in pixels: the anchors arrive
		// in panel units and are converted here.
		float s = Panel.ScaleToScreen;
		painter.TextStyle = plainStyle;
		var glyphs = painter.MeasureText( text );

		float anchorPx = anchorX * s;
		float centerYPx = centerY * s;
		float left = anchorRight ? anchorPx - glyphs.x : anchorPx;
		painter.TextStyle = shadowStyle with { Alignment = TextFlag.Center | TextFlag.DontClip };
		painter.Text( text, new Rect( left, centerYPx - glyphs.y * 0.5f, glyphs.x, glyphs.y ) );
	}

	/// <summary>"YOUR SCORE: 0042" plus, on an exact-zero score, the crooked WASTED stamp over it.</summary>
	void DrawScoreHelper( Painter painter )
	{
		bool wasted = IsWasted;

		float s = Panel.ScaleToScreen;

		painter.Stroke = Stroke.None;
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, YourScoreSize, UiTheme.Yellow, TextFlag.Center );
		painter.Text( $"YOUR SCORE: {ScoreText}", _yourScoreRect.ToScreen( s ) );

		if ( !wasted )
			return;

		// Strikethrough over the digits (the scss line-through).
		var yourScorePx = _yourScoreRect.ToScreen( s );
		var measure = painter.MeasureText( $"YOUR SCORE: {ScoreText}" );
		float strikeWidth = MathF.Min( measure.x, yourScorePx.Width );
		using ( painter.Scope() )
		{
			painter.Fill = UiTheme.Yellow;
			painter.Rect( new Rect( yourScorePx.Center.x - strikeWidth * 0.5f, yourScorePx.Center.y - 2f * s, strikeWidth, 4f * s ) );
		}

		// Crooked WASTED stamp, rotated about its own center.
		var stamp = new Rect( _yourScoreRect.Left, _yourScoreRect.Top - 10f, _yourScoreRect.Width, 90f * 1.2f ).ToScreen( s );
		using ( painter.Scope() )
		{
			painter.Translate( stamp.Center );
			painter.Rotate( WastedRotationDeg );
			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, 90f, UiTheme.Orange, TextFlag.Center )
				.WithShadow( UiTheme.Orange.WithAlpha( 0.6f ), 10f, Vector2.Zero );
			painter.Text( "WASTED", new Rect( -stamp.Width * 0.5f, -stamp.Height * 0.5f, stamp.Width, stamp.Height ) );
		}
	}

	/// <summary>Beers Drunk (magenta) and Bars Visited (blue), both in the display font.</summary>
	void DrawStatsHelper( Painter painter )
	{
		float s = Panel.ScaleToScreen;
		float lineHeight = StatSize * 1.2f;
		var beersRect = new Rect( _statsRect.Left, _statsRect.Top, _statsRect.Width, lineHeight );
		var barsRect = new Rect( _statsRect.Left, _statsRect.Top + lineHeight + StatGap, _statsRect.Width, lineHeight );

		painter.Stroke = Stroke.None;
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, StatSize, UiTheme.Magenta, TextFlag.Center );
		painter.Text( $"Beers Drunk: {BeersText}", beersRect.ToScreen( s ) );

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, StatSize, UiTheme.MediumBlue, TextFlag.Center );
		painter.Text( $"Bars Visited: {BarsVisited}", barsRect.ToScreen( s ) );
	}

	/// <summary>TRY AGAIN: solid magenta, white label, magenta glow on hover.</summary>
	void DrawTryAgainHelper( PaintButton button, Painter painter )
	{
		using ( painter.Scope() )
		{
			float s = button.ScaleToScreen;
			var bounds = painter.Bounds;
			var corners = new Painter.CornerRadii( ButtonRadius * s );

			if ( button.HasActive )
				painter.Opacity = 0.8f;

			if ( button.HasHovered )
				painter.RectShadow( bounds, corners, UiTheme.Magenta.WithAlpha( 0.8f ), blur: 20f * s );

			painter.Fill = UiTheme.Magenta;
			painter.Stroke = Stroke.None;
			painter.Rect( bounds, corners );

			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, ButtonTextSize, Color.White, TextFlag.Center );
			painter.Text( button.Label, bounds );
		}
	}

	/// <summary>QUIT: transparent with an orange border, fills orange with a white label on hover.</summary>
	void DrawQuitHelper( PaintButton button, Painter painter )
	{
		using ( painter.Scope() )
		{
			float s = button.ScaleToScreen;
			var bounds = painter.Bounds;
			var corners = new Painter.CornerRadii( ButtonRadius * s );
			bool hovered = button.HasHovered;

			if ( button.HasActive )
				painter.Opacity = 0.8f;

			painter.Fill = hovered ? UiTheme.Orange : Color.Transparent;
			painter.Stroke = hovered ? Stroke.None : Stroke.Solid( UiTheme.Orange, 2f * s );
			painter.Rect( bounds, corners );

			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, ButtonTextSize, hovered ? Color.White : UiTheme.Orange, TextFlag.Center );
			painter.Text( button.Label, bounds );
		}
	}
}
