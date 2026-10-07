// Project: sboxgamejam3
// File:    AICarTelemetryExporter.cs
// Author:  cseliot
// Created: 2026.10.05
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//

using System;
using System.IO;

/// <summary>
/// Editor half of the LIVE AI car feedback loop (see AI-Car-Feedback-Loop.md). The developer starts
/// play once and leaves it running; a local agent reads telemetry and changes tuning or code while
/// play continues. This class closes the editor side of that loop, every frame, playing or not:
/// <list type="number">
/// <item>Files queued in <see cref="AICarTelemetrySink"/> are copied out of the sandbox into
/// &lt;project&gt;/.aicar-telemetry/ (git-ignored) as soon as they change.</item>
/// <item>&lt;project&gt;/.aicar-telemetry/tuning.json is relayed into the game through
/// <see cref="AICarTelemetrySink.SetTuning"/>; the driver re-applies it on its next tick.</item>
/// <item>Any change to &lt;project&gt;/Code/**.cs|.razor (edit, revert, add, delete) marks the game
/// compiler for a rebuild, so code fixes hotload mid-play even where the engine's own file watch
/// does not fire (Linux).</item>
/// </list>
/// </summary>
public static class AICarTelemetryExporter
{
	private const float PollInterval = 1f;
	private const float RecompileDebounce = 2f;
	// A tuning.json younger than this may still be mid-write; wait for it to settle.
	private static readonly TimeSpan TuningSettle = TimeSpan.FromSeconds( 1 );

	private static int _seenVersion = -1;
	private static RealTimeSince _sincePoll = 1000f;

	// Tuning relay.
	private static DateTime _tuningMtime = DateTime.MinValue;
	private static string _tuningText;
	private static bool _tuningPresent;

	// Code watch.
	private static ulong _codeFingerprint;
	private static bool _codeScanned;
	private static bool _codePending;
	private static RealTimeSince _sinceCodeChange;

	[EditorEvent.Frame]
	public static void Frame()
	{
		string root = Project.Current?.GetRootPath();
		if ( string.IsNullOrEmpty( root ) ) return;

		if ( AICarTelemetrySink.Version != _seenVersion )
		{
			_seenVersion = AICarTelemetrySink.Version;
			WriteLocalHelper( root );
		}

		if ( _sincePoll < PollInterval ) return;
		_sincePoll = 0f;

		PollTuningHelper( root );
		PollCodeHelper( root );
	}

	private static string LocalDir( string root ) => Path.Combine( root, ".aicar-telemetry" );

	/// <summary>Drains the sink into the git-ignored local folder.</summary>
	private static void WriteLocalHelper( string root )
	{
		var files = AICarTelemetrySink.TakePending();
		if ( files.Count == 0 ) return;

		try
		{
			string local = LocalDir( root );
			foreach ( var file in files )
			{
				string path = Path.Combine( local, file.Key.Replace( '/', Path.DirectorySeparatorChar ) );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllText( path, file.Value );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarTelemetryExporter: writing local telemetry failed: {e.Message}" );
		}
	}

	// ================================================================== Tuning relay

	private static void PollTuningHelper( string root )
	{
		string path = Path.Combine( LocalDir( root ), "tuning.json" );

		try
		{
			if ( !File.Exists( path ) )
			{
				if ( !_tuningPresent ) return;
				_tuningPresent = false;
				_tuningText = null;
				_tuningMtime = DateTime.MinValue;
				AICarTelemetrySink.SetTuning( null );
				Log.Info( "AICarTelemetryExporter: tuning.json removed, file tuning cleared." );
				return;
			}

			DateTime mtime = File.GetLastWriteTimeUtc( path );
			if ( _tuningPresent && mtime == _tuningMtime ) return;

			// Written within the last second: possibly half-written (in-place editors). Leave the
			// recorded mtime alone so the next poll looks again once it has settled.
			if ( DateTime.UtcNow - mtime < TuningSettle ) return;

			string text = File.ReadAllText( path );
			_tuningMtime = mtime;
			_tuningPresent = true;
			if ( text == _tuningText ) return;

			_tuningText = text;
			AICarTelemetrySink.SetTuning( text );
			Log.Info( $"AICarTelemetryExporter: tuning.json relayed (revision {RevisionOfHelper( text )}, {text.Length} chars)." );
		}
		catch ( IOException )
		{
			// Locked or vanished mid-poll; the next poll picks it up.
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarTelemetryExporter: reading tuning.json failed: {e.Message}" );
		}
	}

	/// <summary>Best-effort "revision" value for the log line only; the game side does the real parse.</summary>
	private static string RevisionOfHelper( string text )
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse( text );
			if ( doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
				&& doc.RootElement.TryGetProperty( "revision", out var revision ) )
				return revision.ToString();
			return "(none)";
		}
		catch
		{
			return "(malformed json)";
		}
	}

	// ================================================================== Code watch

	private static void PollCodeHelper( string root )
	{
		string code = Path.Combine( root, "Code" );
		if ( !Directory.Exists( code ) ) return;

		ulong fingerprint = 14695981039346656037UL;
		FingerprintDirectoryHelper( code, code.Length, ref fingerprint );

		// First scan only records the baseline.
		if ( !_codeScanned )
		{
			_codeScanned = true;
			_codeFingerprint = fingerprint;
			return;
		}

		// Any difference counts, not just a newer max mtime: a revert copied back from a backup keeps
		// its old mtime, and an add or delete may not move the max at all.
		if ( fingerprint != _codeFingerprint )
		{
			_codeFingerprint = fingerprint;
			_codePending = true;
			_sinceCodeChange = 0f;
			return;
		}

		if ( !_codePending || _sinceCodeChange < RecompileDebounce ) return;
		_codePending = false;

		// Always mark, even if some hotload happened meanwhile: EditorEvent.Hotload fires for ANY
		// assembly (other addons, editor code), so it can't prove the game code was rebuilt. If the
		// engine's own watcher already built this change, the worst case is one redundant rebuild.
		// Marking twice is harmless: the compile group's recompile list is a set, and the per-frame
		// project tick skips while a build is running.
		var compiler = Editor.EditorUtility.Projects.ResolveCompiler( typeof( Sandbox.AICarDriver ).Assembly );
		if ( compiler is null )
		{
			Log.Warning( "AICarTelemetryExporter: Code/ changed but no compiler resolved for the game assembly; recompile not forced." );
			return;
		}

		compiler.MarkForRecompile();
		Log.Info( "AICarTelemetryExporter: Code/ changed, marked the game assembly for recompile." );
	}

	/// <summary>
	/// Folds (relative path, size, mtime) of every .cs/.razor under <paramref name="dir"/> into an
	/// FNV-1a hash. Prunes obj/bin and dot-folders during traversal, and skips any single file or
	/// folder that disappears mid-scan instead of aborting the whole poll.
	/// </summary>
	private static void FingerprintDirectoryHelper( string dir, int rootLength, ref ulong hash )
	{
		string[] files;
		string[] subdirs;
		try
		{
			files = Directory.GetFiles( dir );
			subdirs = Directory.GetDirectories( dir );
		}
		catch ( Exception )
		{
			return;
		}

		// Sorted so the hash doesn't depend on filesystem enumeration order.
		Array.Sort( files, StringComparer.Ordinal );
		Array.Sort( subdirs, StringComparer.Ordinal );

		foreach ( string file in files )
		{
			if ( !file.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) && !file.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) )
				continue;

			try
			{
				var info = new FileInfo( file );
				if ( !info.Exists ) continue;
				HashStringHelper( ref hash, file.Substring( rootLength ) );
				HashLongHelper( ref hash, info.Length );
				HashLongHelper( ref hash, info.LastWriteTimeUtc.Ticks );
			}
			catch ( Exception )
			{
				// Deleted or locked between listing and stat: the next poll sees the settled state.
			}
		}

		foreach ( string sub in subdirs )
		{
			string name = Path.GetFileName( sub );
			if ( name.Equals( "obj", StringComparison.OrdinalIgnoreCase )
				|| name.Equals( "bin", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( '.' ) )
				continue;
			FingerprintDirectoryHelper( sub, rootLength, ref hash );
		}
	}

	private static void HashStringHelper( ref ulong hash, string value )
	{
		foreach ( char c in value )
		{
			hash ^= c;
			hash *= 1099511628211UL;
		}
		hash ^= 0xFF; // separator so "ab"+"c" != "a"+"bc"
		hash *= 1099511628211UL;
	}

	private static void HashLongHelper( ref ulong hash, long value )
	{
		for ( int i = 0; i < 8; i++ )
		{
			hash ^= (byte)(value >> (i * 8));
			hash *= 1099511628211UL;
		}
	}
}
