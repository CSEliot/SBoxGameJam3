// Project: sboxgamejam3
// File:    AICarTelemetrySink.cs
// Author:  cseliot
// Created: 2026.10.05
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 

using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Hand-off point for <see cref="AICarDriver"/> telemetry files. Every file is written to
/// FileSystem.Data (always works, even in a published game) and queued in memory for the editor
/// exporter (Editor/AICarTelemetryExporter.cs), which copies queued files out of the sandbox into
/// the telemetry git worktree. Paths are relative, forward-slashed, and the latest text wins.
/// </summary>
public static class AICarTelemetrySink
{
	private static readonly Dictionary<string, string> _pending = new();
	private static bool _warnedWriteFailed;

	/// <summary>Increments on every <see cref="Put"/>; lets the exporter notice new data cheaply.</summary>
	public static int Version { get; private set; }

	public static void Put( string relativePath, string text )
	{
		_pending[relativePath] = text;
		Version++;

		try
		{
			int slash = relativePath.LastIndexOf( '/' );
			if ( slash > 0 ) FileSystem.Data.CreateDirectory( relativePath[..slash] );
			FileSystem.Data.WriteAllText( relativePath, text );
		}
		catch ( System.Exception e )
		{
			if ( _warnedWriteFailed ) return;
			_warnedWriteFailed = true;
			Log.Warning( $"AICarTelemetrySink: could not write '{relativePath}' to FileSystem.Data: {e.Message}" );
		}
	}

	/// <summary>Removes and returns every queued file (editor exporter only).</summary>
	public static List<KeyValuePair<string, string>> TakePending()
	{
		var files = new List<KeyValuePair<string, string>>( _pending );
		_pending.Clear();
		return files;
	}
}
