// Project: sboxgamejam3
// File:    Minigame.cs
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
// Bar mini-game overlay (Design.md, Screen 3). Spacebar mashing fills a
// progress bar that decays over time. Each time the bar hits 100% it resets
// and one beer is added. The round runs for a fixed duration; OnFinished
// reports the beers drunk. This file holds the display-formatting members and
// a code-built panel tree whose every visual is drawn with Painter - there is
// no razor markup or stylesheet. Game state and simulation live in
// Minigame.Logic.cs.

using System;
using Sandbox;
using Sandbox.UI;

namespace Sandbox.UI;

public partial class Minigame : PaintPanelComponent
{
	// DISPLAY-ONLY: formatting values and the code-built panel tree. Game state
	// (Progress, Beers, _punch, _endsAt) and all simulation logic live in
	// Minigame.Logic.cs.

	int Percent => (int)MathF.Round( Progress * 100f );
	int SecondsLeft => Math.Max( 0, (int)MathF.Ceiling( _endsAt.Relative ) );
	float TitleScale => 1f + 0.15f * _punch;

	// Layout in panel units, taken from the former stylesheet.
	const float Pad = 60f;
	const float TitleFontSize = 80f;
	const float TextFontSize = 60f;
	const float BarWidth = 600f;
	const float BarHeight = 40f;
	const float BarBorderWidth = 2f;
	const float PercentMinWidth = 150f;
	const float RowGap = 20f;
	const float GroupGap = 12f;
	const float LineHeight = 1.2f;
	const float PulseSeconds = 0.6f;
	const float PulseLowOpacity = 0.35f;

	Panel _builtFor;
	PaintCanvas _canvas;

	Rect _titleRect;
	Rect _barRect;
	Rect _percentRect;
	Rect _beersRect;
	Rect _instructionRect;
	Rect _timerRect;
	Vector2 _layoutSize;

	/// <summary>
	/// Builds the drawing canvas into the component's root panel. PaintPanelComponent can
	/// recreate the root without OnEnabled, so this is re-checked every frame.
	/// </summary>
	void EnsureTreeHelper()
	{
		if ( Panel is null || ReferenceEquals( Panel, _builtFor ) ) return;

		_builtFor = Panel;
		Panel.MakeFullscreen();
		Panel.Style.PointerEvents = PointerEvents.None;

		_canvas = new PaintCanvas { Draw = DrawHelper };
		Panel.AddChild( _canvas );
	}

	protected override void OnPreRender()
	{
		EnsureTreeHelper();

		if ( Panel is null ) return;

		// Pure readout: the overlay only exists while a round is running.
		Panel.SetVisible( IsPlaying );

		if ( !IsPlaying ) return;

		LayoutHelper( Panel.LocalSize() );
	}

	/// <summary>
	/// Computes every element rect for a given panel size. Mirrors the old flex
	/// column: title pinned to the top, bottom group pinned to the bottom, middle
	/// group centred in the remaining space.
	/// </summary>
	void LayoutHelper( Vector2 size )
	{
		_layoutSize = size;

		float centerX = size.x * 0.5f;
		float titleLine = TitleFontSize * LineHeight;
		float textLine = TextFontSize * LineHeight;

		float rowHeight = MathF.Max( BarHeight, textLine );
		float middleHeight = rowHeight + GroupGap + textLine;
		float bottomHeight = textLine + GroupGap + textLine;

		float titleTop = Pad;
		float titleBottom = titleTop + titleLine;
		float bottomTop = size.y - Pad - bottomHeight;
		float middleTop = titleBottom + ( bottomTop - titleBottom - middleHeight ) * 0.5f;

		_titleRect = new Rect( 0f, titleTop, size.x, titleLine );

		float rowWidth = BarWidth + RowGap + PercentMinWidth;
		float rowLeft = centerX - rowWidth * 0.5f;
		_barRect = new Rect( rowLeft, middleTop + ( rowHeight - BarHeight ) * 0.5f, BarWidth, BarHeight );
		_percentRect = new Rect( rowLeft + BarWidth + RowGap, middleTop, PercentMinWidth, textLine );
		_beersRect = new Rect( 0f, middleTop + rowHeight + GroupGap, size.x, textLine );

		_instructionRect = new Rect( 0f, bottomTop, size.x, textLine );
		_timerRect = new Rect( 0f, bottomTop + textLine + GroupGap, size.x, textLine );
	}

	/// <summary>
	/// The canvas draw callback. Repaints the whole overlay every frame.
	/// </summary>
	void DrawHelper( Painter painter )
	{
		// Painter draws in screen pixels; layout and PlaceAt work in panel units.
		var s = Panel.ScaleToScreen;
		var size = painter.Bounds.Size / s;

		if ( size.x <= 0f || size.y <= 0f ) return;

		if ( size != _layoutSize )
			LayoutHelper( size );

		DrawTitleHelper( painter, s );
		DrawBarHelper( painter, s );
		DrawMiddleTextHelper( painter, s );
		DrawBottomHelper( painter, s );
	}

	/// <summary>
	/// "CHUG! CHUG! CHUG!" scaled around its own centre for the spacebar punch.
	/// </summary>
	void DrawTitleHelper( Painter painter, float s )
	{
		var center = _titleRect.Center;

		using ( painter.Scope() )
		{
			// Translate to the rect centre in screen pixels, then apply the punch scale.
			painter.Translate( center.x * s, center.y * s );
			painter.Scale( TitleScale );

			painter.TextStyle = new TextStyle
			{
				FontName = UiTheme.DisplayFont,
				FontSize = TitleFontSize,
				Color = UiTheme.Magenta,
				Alignment = TextFlag.Center | TextFlag.DontClip,
			};

			var area = new Rect(
				-_titleRect.Width * 0.5f * s,
				-_titleRect.Height * 0.5f * s,
				_titleRect.Width * s,
				_titleRect.Height * s );

			painter.Text( "CHUG! CHUG! CHUG!", area );
		}
	}

	/// <summary>
	/// The 600px bar: a full-width MediumBlue to Magenta gradient revealed from the
	/// left by the fill width, with a 2px MediumBlue inset border on top.
	/// </summary>
	void DrawBarHelper( Painter painter, float s )
	{
		var inner = _barRect.Shrink( BarBorderWidth );
		float fillWidth = inner.Width * Math.Clamp( Percent / 100f, 0f, 1f );

		if ( fillWidth > 0f )
		{
			using ( painter.Scope() )
			{
				painter.Clip( new Rect( inner.Left, inner.Top, fillWidth, inner.Height ).ToScreen( s ) );
				painter.Fill = Fill.LinearGradient( UiTheme.MediumBlue, UiTheme.Magenta );
				painter.Stroke = Stroke.None;
				painter.Rect( inner.ToScreen( s ) );
			}
		}

		painter.Fill = Fill.None;
		painter.Stroke = Stroke.Solid( UiTheme.MediumBlue, BarBorderWidth * s )
			.WithAlignment( Stroke.StrokeAlignment.Inside );
		painter.Rect( _barRect.ToScreen( s ) );
	}

	/// <summary>
	/// The yellow percentage readout and the magenta beer total.
	/// </summary>
	void DrawMiddleTextHelper( Painter painter, float s )
	{
		painter.Stroke = Stroke.None;

		painter.TextStyle = new TextStyle
		{
			FontName = UiTheme.BodyFont,
			FontSize = TextFontSize,
			Color = UiTheme.Yellow,
			Alignment = TextFlag.LeftCenter | TextFlag.DontClip,
		};
		painter.Text( $"{Percent}%", _percentRect.ToScreen( s ) );

		painter.TextStyle = new TextStyle
		{
			FontName = UiTheme.BodyFont,
			FontSize = TextFontSize,
			Color = UiTheme.Magenta,
			Alignment = TextFlag.Center | TextFlag.DontClip,
		};
		painter.Text( $"{Beers.ToString( "F2" )} BEERS", _beersRect.ToScreen( s ) );
	}

	/// <summary>
	/// The pulsing orange prompt and the timer that turns orange under 3 seconds.
	/// </summary>
	void DrawBottomHelper( Painter painter, float s )
	{
		painter.Stroke = Stroke.None;

		// Opacity pulse 1 -> 0.35 -> 1 every PulseSeconds, matching the old keyframes.
		float phase = RealTime.Now / PulseSeconds;
		float pulse = PulseLowOpacity + ( 1f - PulseLowOpacity ) * ( 0.5f + 0.5f * MathF.Cos( MathF.Tau * phase ) );

		using ( painter.Scope() )
		{
			painter.Opacity = pulse;
			painter.TextStyle = new TextStyle
			{
				FontName = UiTheme.BodyFont,
				FontSize = TextFontSize,
				Color = UiTheme.Orange,
				Alignment = TextFlag.Center | TextFlag.DontClip,
			};
			painter.Text( "SMASH SPACEBAR!", _instructionRect.ToScreen( s ) );
		}

		painter.TextStyle = new TextStyle
		{
			FontName = UiTheme.BodyFont,
			FontSize = TextFontSize,
			Color = SecondsLeft < 3 ? UiTheme.Orange : UiTheme.Yellow,
			Alignment = TextFlag.Center | TextFlag.DontClip,
		};
		painter.Text( $"TIME: {SecondsLeft}s", _timerRect.ToScreen( s ) );
	}
}
