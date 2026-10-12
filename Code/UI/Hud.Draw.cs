// Project: sboxgamejam3
// File:    Hud.Draw.cs
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
// Drawing half of the pub-crawling HUD (Design.md, Screen 1). Builds the panel tree in code
// (EnsureTreeHelper), shares one set of layout metrics between the click-through Painter
// canvas and the bottom-left target-bar ScenePanel (LayoutHelper), and paints the whole
// readout - player list column, timer column, beers column, the paused banner, the automatic
// pants-down warning, the build watermark and the target-bar preview frame + caption
// (DrawHelper). The live 3D preview itself stays a real ScenePanel: Painter cannot draw a
// scene. Flat overlay straight on the fullscreen root, sizes and colours taken from the old
// stylesheet, all text positions computed from Painter.MeasureText since there is no flex
// layout to lean on.
//
// Units: every metric below is in PANEL UNITS (a 1080-tall reference; the stylesheet's px).
// LayoutHelper, PlaceAt and the metrics all work in panel units. Painter draws in SCREEN
// PIXELS, so the painter boundary converts: the draw entry points take Panel.ScaleToScreen,
// every rect goes through Rect.ToScreen( s ), font sizes stay in panel units (the engine scales
// TextStyle by ScaleToScreen when it lays the text out), and a measured text width is divided
// by s to get back to panel units. Do not scale the whole
// screen with painter.Scale - text would be laid out unscaled and then stretched.
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

public partial class Hud
{
	// Layout metrics, straight from the stylesheet (panel units).
	private const float RowPaddingX = 60f;      // .row padding 40px 60px
	private const float RowPaddingTop = 40f;
	private const float PlayerRowGap = 16f;     // .pl-row gap
	private const float LabelSize = 24f;        // .stat .label font-size
	private const float LabelLine = 30f;        // label line box (approx)
	private const float PlayerNameSize = 24f;   // .pl-name font-size
	private const float PlayerScoreSize = 32f;  // .pl-score font-size
	private const float PlayerRowHeight = 40f;  // row line box: max(name, score)
	private const float TimerValueSize = 80f;   // .stat.timer .value font-size
	private const float TimerValueHeight = 100f;
	private const float BeersValueSize = 60f;   // .stat.drunk .value font-size
	private const float BeersValueHeight = 75f;
	private const float PausedBannerSize = 40f; // .paused-banner font-size
	private const float PausedRowOpacity = 0.5f;
	private const float VersionSize = 16f;      // .version-label font-size
	private const float VersionRight = 20f;
	private const float VersionBottom = 16f;
	private const float PantsSize = 48f;        // .pants-debug font-size
	private const float PantsPadX = 24f;        // .pants-debug padding 12px 24px
	private const float PantsPadY = 12f;
	private const float PantsRight = 40f;       // .pants-debug-col right
	private const float PreviewLeft = 24f;      // .bar-preview-frame left
	private const float PreviewBottom = 12f;    // .bar-preview-frame bottom
	private const float PreviewSize = 150f;     // .bar-preview-frame width/height
	private const float PreviewBorder = 2f;     // .bar-preview-frame border
	private const float PreviewCaptionSize = 9f;

	/// <summary>Timer flash bottom opacity (hud-flash keyframe: 1 -> 0.3 -> 1 over 0.5s).</summary>
	private const float TimerFlashMinOpacity = 0.3f;

	/// <summary>The preview frame's existing red, reused from the pants-down warning.</summary>
	private static readonly Color PreviewRed = Color.FromRgb( 0xD42020 );

	/// <summary>
	/// The beer glyph's family. Neither First Blood nor Snow Blue carries an emoji glyph, so the
	/// glyph is drawn as its own run in a system emoji family (the old stylesheet scoped the
	/// same fallback onto the beer span). The stylesheet listed "Segoe UI Emoji" first and the
	/// engine's CSS font-family only uses the first family of a fallback stack, so that is what
	/// the old HUD actually asked for.
	/// </summary>
	private const string EmojiFont = "Segoe UI Emoji";

	/// <summary>The beer mug glyph (U+1F37A).</summary>
	private const string BeerGlyph = "\U0001F37A";

	/// <summary>pants-debug .up colours.</summary>
	private static readonly Color PantsUpFill = Color.FromRgb( 0x35D435 );

	/// <summary>
	/// Builds the panel tree into the PaintPanelComponent's own root: the root itself, the
	/// bottom-left target-bar preview host and the fullscreen Painter canvas. The root Panel is
	/// created fresh by PaintPanelComponent whenever the component is re-enabled (and can be
	/// recreated without OnEnabled), so the tree is rebuilt off the root's identity rather than
	/// cached across lifetimes. The preview host is added BEFORE the canvas so the frame and its
	/// caption paint on top of the live scene composite.
	/// </summary>
	private void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) )
			return;

		_builtFor = Panel;

		Panel.MakeFullscreen();
		// Non-interactive readout: never eat clicks meant for the world or another panel.
		Panel.Style.PointerEvents = PointerEvents.None;

		_previewHost = new ScenePanel();
		_previewHost.Style.PointerEvents = PointerEvents.None;
		Panel.AddChild( _previewHost );

		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );

		// The cached preview scene belonged to the previous host panel; the new one owns a
		// brand new RenderScene (UpdateTargetBarPreviewHelper rebuilds into it).
		ClearTargetBarPreviewCacheHelper();
		LayoutHelper( Panel.LocalSize() );
	}

	/// <summary>
	/// Computes the shared layout metrics from a panel-unit size and places the preview host.
	/// Called from OnUpdate with Panel.LocalSize() (panel units) and from DrawHelper with the
	/// painter's bounds converted back to panel units (painter.Bounds.Size / s), so drawing and
	/// placement agree. Sizes and insets are the old stylesheet's px values in panel units:
	/// PlaceAt consumes them directly, and the draw multiplies by ScaleToScreen at the end.
	/// </summary>
	private void LayoutHelper( Vector2 size )
	{
		if ( size.x <= 0f || size.y <= 0f )
			return;

		_layoutSize = size;

		// .row is 60px inset on both sides; the three .stat columns are equal flex 1 1 0, so
		// the middle column is centred on the panel regardless of the neighbours' text width.
		_colWidth = MathF.Max( ( size.x - RowPaddingX * 2f ) / 3f, 1f );
		_col1X = RowPaddingX;
		_col2CenterX = size.x * 0.5f;
		_col3RightX = size.x - RowPaddingX;
		_rowTop = RowPaddingTop;

		if ( _previewHost is null )
			return;

		// The ScenePanel sits inside the frame's border, so the drawn frame overlaps the
		// outer border width rather than the live 3D preview (the old preview was inset too).
		_previewHost.PlaceAt( new Rect(
			PreviewLeft + PreviewBorder,
			size.y - PreviewBottom - PreviewSize + PreviewBorder,
			PreviewSize - PreviewBorder * 2f,
			PreviewSize - PreviewBorder * 2f ) );
	}

	/// <summary>
	/// Paints the whole HUD. Everything is drawn here rather than by panel styles, so the
	/// blocks keep their own visibility conditions: the run readout follows Visible, the pants
	/// warning and the build watermark stay up while it is closed (as the old markup had them).
	/// </summary>
	private void DrawHelper( Painter painter )
	{
		// Painter draws in screen pixels, the HUD lays out in panel units, so the painter's
		// bounds come back to panel units here and every drawn rect goes out through ToScreen.
		float s = Panel?.ScaleToScreen ?? 1f;
		if ( s <= 0f )
			s = 1f;

		var size = painter.Bounds.Size / s;
		if ( size != _layoutSize )
			LayoutHelper( size );

		if ( size.x <= 0f || size.y <= 0f )
			return;

		if ( Visible )
			DrawRunReadoutHelper( painter, size, s );

		if ( PantsDebugShown )
			DrawPantsWarningHelper( painter, size, s );

		if ( !string.IsNullOrWhiteSpace( _VersionLabel ) )
			DrawVersionLabelHelper( painter, size, s );

		if ( Visible )
			DrawTargetBarFrameHelper( painter, s );
	}

	/// <summary>
	/// The three-column run row plus the paused banner. The row dims while the timer is paused
	/// (Design #45); the banner sits below the row and is NOT dimmed (it is a sibling of the
	/// row in the old markup).
	/// </summary>
	private void DrawRunReadoutHelper( Painter painter, Vector2 size, float s )
	{
		using ( painter.Scope() )
		{
			if ( IsPaused )
				painter.Opacity = PausedRowOpacity;

			DrawPlayerColumnHelper( painter, s );
			DrawTimerColumnHelper( painter, s );
			DrawBeersColumnHelper( painter, s );
		}

		if ( !IsPaused )
			return;

		float rowHeight = RowContentHeightHelper();
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, PausedBannerSize, UiTheme.Orange, TextFlag.Center );
		painter.Text( "TIMER PAUSED", new Rect(
			_col1X,
			_rowTop + rowHeight + RowPaddingTop,
			size.x - RowPaddingX * 2f,
			PausedBannerSize * 1.25f ).ToScreen( s ) );
	}

	/// <summary>Tallest of the three columns, which sets the paused banner's y.</summary>
	private float RowContentHeightHelper()
	{
		float players = LabelLine + _cachedPlayerRows.Count * PlayerRowHeight;
		return MathF.Max( players, MathF.Max( LabelLine + TimerValueHeight, LabelLine + BeersValueHeight ) );
	}

	/// <summary>
	/// Top-left column: the PLAYERS label and one row per connected player (name, floored
	/// score). The local row's name is magenta; every other name is a dimmed white. Rows are
	/// the throttled copies (see OnUpdate), so digits tick calmly. Metrics are panel units;
	/// the measured name width is painter pixels and is divided by s to match.
	/// </summary>
	private void DrawPlayerColumnHelper( Painter painter, float s )
	{
		float x = _col1X;
		float y = _rowTop;

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, LabelSize, UiTheme.MediumBlue );
		painter.Text( "PLAYERS", new Rect( x, y, _colWidth, LabelLine ).ToScreen( s ) );
		y += LabelLine;

		foreach ( var row in _cachedPlayerRows )
		{
			painter.TextStyle = UiTheme.Text(
				UiTheme.BodyFont,
				PlayerNameSize,
				row.IsLocal ? UiTheme.Magenta : Color.White.WithAlpha( 0.85f ) );

			// The row is 40px tall for the 32px score; nudge the 24px name down to roughly
			// centre it against the score, matching the old align-items: center.
			painter.Text( row.Name, new Rect( x, y + 5f, _colWidth, PlayerNameSize * 1.25f ).ToScreen( s ) );

			float nameWidth = painter.MeasureText( row.Name ).x / s;

			painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, PlayerScoreSize, UiTheme.Yellow );
			painter.Text(
				row.Score.ToString( "D4" ),
				new Rect( x + nameWidth + PlayerRowGap, y, MathF.Max( _colWidth - nameWidth - PlayerRowGap, 1f ), PlayerRowHeight ).ToScreen( s ) );

			y += PlayerRowHeight;
		}
	}

	/// <summary>
	/// Top-center column: the TIME label and the M:SS value. Yellow normally, orange under
	/// _TimerWarnSeconds, and orange flashing under _TimerCriticalSeconds (hud-flash).
	/// </summary>
	private void DrawTimerColumnHelper( Painter painter, float s )
	{
		float width = _colWidth;
		float left = _col2CenterX - width * 0.5f;

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, LabelSize, UiTheme.MediumBlue, TextFlag.Center );
		painter.Text( "TIME", new Rect( left, _rowTop, width, LabelLine ).ToScreen( s ) );

		bool critical = TimerSeconds < _TimerCriticalSeconds;
		bool warning = TimerSeconds < _TimerWarnSeconds;

		using ( painter.Scope() )
		{
			if ( critical )
				painter.Opacity = TimerFlashOpacityHelper();

			painter.TextStyle = UiTheme.Text(
				UiTheme.DisplayFont,
				TimerValueSize,
				warning ? UiTheme.Orange : UiTheme.Yellow,
				TextFlag.Center );
			painter.Text( _cachedTimerText, new Rect( left, _rowTop + LabelLine, width, TimerValueHeight ).ToScreen( s ) );
		}
	}

	/// <summary>
	/// Top-right column: the BEERS label and the beer glyph + one-decimal count, right-aligned.
	/// The glyph is its own text run with the system emoji family - the display font has no
	/// emoji glyph, and a fallback family scoped onto the glyph alone is what the old markup did.
	/// Measured widths come back in painter pixels and are divided by s into panel units.
	/// </summary>
	private void DrawBeersColumnHelper( Painter painter, float s )
	{
		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, LabelSize, UiTheme.MediumBlue );
		float labelWidth = painter.MeasureText( "BEERS" ).x / s;
		painter.Text( "BEERS", new Rect( _col3RightX - labelWidth, _rowTop, labelWidth + 1f, LabelLine ).ToScreen( s ) );

		float valueTop = _rowTop + LabelLine;

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, BeersValueSize, UiTheme.Magenta );
		float textWidth = painter.MeasureText( _cachedBeersText ).x / s;

		painter.TextStyle = UiTheme.Text( EmojiFont, BeersValueSize, UiTheme.Magenta );
		float glyphWidth = painter.MeasureText( BeerGlyph ).x / s;

		float left = _col3RightX - glyphWidth - textWidth;

		painter.TextStyle = UiTheme.Text( EmojiFont, BeersValueSize, UiTheme.Magenta );
		painter.Text( BeerGlyph, new Rect( left, valueTop, glyphWidth + 1f, BeersValueHeight ).ToScreen( s ) );

		painter.TextStyle = UiTheme.Text( UiTheme.DisplayFont, BeersValueSize, UiTheme.Magenta );
		painter.Text( _cachedBeersText, new Rect( left + glyphWidth, valueTop, textWidth + 1f, BeersValueHeight ).ToScreen( s ) );
	}

	/// <summary>
	/// Right-edge automatic pants-down warning: a flat block of colour, vertically centred on
	/// the right edge, blinking hard on/off at _PantsDebugBlinksPerSecond while the pants are
	/// down. Sits outside the run gate (only the street gate and the down-timer gate it). The
	/// measured text size is painter pixels and is divided by s before the box is built.
	/// </summary>
	private void DrawPantsWarningHelper( Painter painter, Vector2 size, float s )
	{
		bool down = PantsDebugIsDown;

		painter.TextStyle = UiTheme.Text(
			UiTheme.DisplayFont,
			PantsSize,
			down ? Color.White : Color.Black );
		var textSize = painter.MeasureText( _cachedPantsText ) / s;

		float boxWidth = textSize.x + PantsPadX * 2f;
		float boxHeight = textSize.y + PantsPadY * 2f;

		using ( painter.Scope() )
		{
			if ( down )
			{
				// steps(2, end) blink: hard on for the first half of the cycle, off for the rest.
				float period = PantsBlinkSeconds;
				float phase = ( RealTime.Now % period ) / period;
				painter.Opacity = phase < 0.5f ? 1f : 0f;
			}

			painter.Fill = down ? PreviewRed : PantsUpFill;
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect(
				size.x - PantsRight - boxWidth,
				size.y * 0.5f - boxHeight * 0.5f,
				boxWidth,
				boxHeight ).ToScreen( s ) );

			painter.Fill = Color.Transparent;
			painter.Text( _cachedPantsText, new Rect(
				size.x - PantsRight - boxWidth + PantsPadX,
				size.y * 0.5f - textSize.y * 0.5f,
				textSize.x + 1f,
				textSize.y + 1f ).ToScreen( s ) );
		}
	}

	/// <summary>Build watermark, small and dim in the bottom-right. Shows on the menu too.</summary>
	private void DrawVersionLabelHelper( Painter painter, Vector2 size, float s )
	{
		painter.TextStyle = UiTheme.Text( UiTheme.BodyFont, VersionSize, Color.White.WithAlpha( 0.4f ) );
		var textSize = painter.MeasureText( _VersionLabel ) / s;
		painter.Text( _VersionLabel, new Rect(
			size.x - VersionRight - textSize.x,
			size.y - VersionBottom - textSize.y,
			textSize.x + 1f,
			textSize.y + 1f ).ToScreen( s ) );
	}

	/// <summary>
	/// Red border-only frame around the live target-bar preview, with the caption straddling
	/// the bottom border line. The ScenePanel fills this box (TickTargetBarPreviewHelper keeps
	/// the camera's render size matched to it). The caption is drawn after the border so it
	/// crosses it rather than being hidden behind it. The frame is built in panel units
	/// (_layoutSize is panel units) and converted by the painter-boundary ToScreen.
	/// </summary>
	private void DrawTargetBarFrameHelper( Painter painter, float s )
	{
		var frame = new Rect(
			PreviewLeft,
			_layoutSize.y - PreviewBottom - PreviewSize,
			PreviewSize,
			PreviewSize );

		painter.Fill = Color.Transparent;
		painter.Stroke = Stroke.Solid( PreviewRed, PreviewBorder * s );
		painter.Rect( frame.ToScreen( s ) );

		painter.Stroke = Stroke.None;
		painter.TextStyle = UiTheme.Text( UiTheme.BodyFont, PreviewCaptionSize, PreviewRed, TextFlag.Center );
		painter.Text( "YOUR NEXT BAR IS", new Rect(
			frame.Left,
			frame.Bottom - PreviewCaptionSize * 0.75f,
			frame.Width,
			PreviewCaptionSize * 1.5f ).ToScreen( s ) );
	}
}
