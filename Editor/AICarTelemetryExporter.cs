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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Editor half of the AI car feedback loop (see AI-Car-Feedback-Loop.md). The only thing the
/// developer does is start and stop play; this closes the rest of the loop:
/// <list type="number">
/// <item>While playing, files queued in <see cref="AICarTelemetrySink"/> are copied out of the
/// sandbox into &lt;project&gt;/.aicar-telemetry/ (git-ignored).</item>
/// <item>After play stops, they are mirrored into a separate git worktree on
/// <see cref="TelemetryBranch"/> (a sibling folder, so your own working tree is never touched),
/// committed and pushed, where the analysis side picks them up.</item>
/// <item>While not playing, every <see cref="PullInterval"/> seconds the project fast-forwards
/// <see cref="CodeBranch"/> from origin, so driver fixes hotload before the next play. Only when
/// that branch is checked out, and only fast-forwards: local edits are never overwritten.</item>
/// </list>
/// Create an empty file &lt;project&gt;/.aicar-telemetry/off to pause the git side.
/// </summary>
public static class AICarTelemetryExporter
{
	public const string CodeBranch = "claude/ai-car-feedback-loop-bxjrbe";
	public const string TelemetryBranch = "claude/ai-car-feedback-loop-bxjrbe-telemetry";
	private const float PullInterval = 30f;

	private static int _seenVersion = -1;
	private static bool _dirty;
	private static bool _busy;
	private static RealTimeSince _sinceWrite;
	private static RealTimeSince _sincePush = 1000f;
	private static RealTimeSince _sincePull = 1000f;
	private static string _lastPullWarning = "";

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

		if ( Game.IsPlaying || _busy ) return;
		if ( File.Exists( Path.Combine( LocalDir( root ), "off" ) ) ) return;

		// The final flush lands while the play scene shuts down: wait for writes to settle.
		if ( _dirty && _sinceWrite > 2f && _sincePush > 10f )
		{
			_dirty = false;
			_sincePush = 0f;
			_busy = true;
			Task.Run( () => RunSafe( () => PushTelemetryHelper( root ) ) );
			return;
		}

		if ( _sincePull > PullInterval )
		{
			_sincePull = 0f;
			_busy = true;
			Task.Run( () => RunSafe( () => PullCodeHelper( root ) ) );
		}
	}

	private static string LocalDir( string root ) => Path.Combine( root, ".aicar-telemetry" );

	private static string WorktreeDir( string root )
	{
		string full = Path.GetFullPath( root ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
		return Path.Combine( Path.GetDirectoryName( full ) ?? full, Path.GetFileName( full ) + "-aicar-telemetry" );
	}

	/// <summary>Main thread: drains the sink into the git-ignored local folder.</summary>
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
			_dirty = true;
			_sinceWrite = 0f;
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarTelemetryExporter: writing local telemetry failed: {e.Message}" );
		}
	}

	private static void RunSafe( Action action )
	{
		try
		{
			action();
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarTelemetryExporter: {e.Message}" );
		}
		finally
		{
			_busy = false;
		}
	}

	// ================================================================== Telemetry push

	private static void PushTelemetryHelper( string root )
	{
		string worktree = WorktreeDir( root );
		if ( !EnsureWorktreeHelper( root, worktree ) ) return;

		string source = Path.Combine( LocalDir( root ), "aicar" );
		if ( !Directory.Exists( source ) ) return;

		// Record which code produced these runs.
		Git( root, "rev-parse --short HEAD", out string sha );
		Git( root, "status --porcelain -- Code Editor", out string dirtyCode );
		string codeInfo = $"code_commit={sha.Trim()}\ncode_dirty={(string.IsNullOrWhiteSpace( dirtyCode ) ? "no" : "yes")}\n";

		foreach ( string sessionDir in Directory.GetDirectories( source ) )
		{
			string relative = Path.GetRelativePath( LocalDir( root ), sessionDir );
			string target = Path.Combine( worktree, relative );
			CopyDirectoryHelper( sessionDir, target );

			string meta = Path.Combine( target, "meta.txt" );
			if ( !File.Exists( meta ) ) File.WriteAllText( meta, codeInfo );
		}

		Git( worktree, "add -A", out _ );
		Git( worktree, "status --porcelain", out string changes );
		if ( string.IsNullOrWhiteSpace( changes ) ) return;

		if ( !Git( worktree, $"commit -q -m \"AI car telemetry {DateTime.Now:yyyy-MM-dd HH:mm:ss}\"", out string commitOut ) )
		{
			Log.Warning( $"AICarTelemetryExporter: telemetry commit failed: {commitOut}" );
			return;
		}

		if ( Git( worktree, $"push -q origin HEAD:refs/heads/{TelemetryBranch}", out string pushOut ) )
			Log.Info( $"AICarTelemetryExporter: telemetry pushed to {TelemetryBranch}." );
		else
		{
			_dirty = true; // retried on the next idle check
			Log.Warning( $"AICarTelemetryExporter: telemetry push failed (will retry): {pushOut}" );
		}
	}

	/// <summary>
	/// Sibling worktree on <see cref="TelemetryBranch"/>: tracks the remote branch if it exists,
	/// otherwise starts it as an orphan branch holding only telemetry.
	/// </summary>
	private static bool EnsureWorktreeHelper( string root, string worktree )
	{
		if ( File.Exists( Path.Combine( worktree, ".git" ) ) ) return true;

		Git( root, "worktree prune", out _ );

		Git( root, $"ls-remote --heads origin {TelemetryBranch}", out string remote );
		bool remoteExists = remote.Contains( TelemetryBranch );

		string quoted = $"\"{worktree}\"";
		if ( remoteExists )
		{
			if ( !Git( root, $"fetch -q origin {TelemetryBranch}", out string fetchOut ) )
			{
				Log.Warning( $"AICarTelemetryExporter: fetching {TelemetryBranch} failed: {fetchOut}" );
				return false;
			}
			if ( !Git( root, $"worktree add -f -B {TelemetryBranch} {quoted} FETCH_HEAD", out string addOut ) )
			{
				Log.Warning( $"AICarTelemetryExporter: creating the telemetry worktree failed: {addOut}" );
				return false;
			}
		}
		else
		{
			if ( !Git( root, $"worktree add -f --detach {quoted} HEAD", out string addOut ) )
			{
				Log.Warning( $"AICarTelemetryExporter: creating the telemetry worktree failed: {addOut}" );
				return false;
			}
			Git( worktree, $"checkout -q --orphan {TelemetryBranch}", out _ );
			Git( worktree, "rm -r -q -f --cached .", out _ );
			Git( worktree, "clean -f -d -q", out _ );
			File.WriteAllText( Path.Combine( worktree, "README.md" ),
				"# AI car telemetry\n\nWritten by Editor/AICarTelemetryExporter.cs after each play session. One folder per session (aicar/<timestamp>/), one subfolder per car. See AI-Car-Feedback-Loop.md on the code branch.\n" );
		}

		Log.Info( $"AICarTelemetryExporter: telemetry worktree ready at {worktree}." );
		return true;
	}

	private static void CopyDirectoryHelper( string from, string to )
	{
		Directory.CreateDirectory( to );
		foreach ( string file in Directory.GetFiles( from ) )
			File.Copy( file, Path.Combine( to, Path.GetFileName( file ) ), true );
		foreach ( string dir in Directory.GetDirectories( from ) )
			CopyDirectoryHelper( dir, Path.Combine( to, Path.GetFileName( dir ) ) );
	}

	// ================================================================== Code pull

	private static void PullCodeHelper( string root )
	{
		if ( !Git( root, "rev-parse --abbrev-ref HEAD", out string branch ) ) return;
		if ( branch.Trim() != CodeBranch ) return;

		if ( !Git( root, $"fetch -q origin {CodeBranch}", out _ ) ) return;

		Git( root, "rev-list --count HEAD..FETCH_HEAD", out string behind );
		if ( behind.Trim() == "0" || string.IsNullOrWhiteSpace( behind ) ) return;

		if ( Git( root, "merge -q --ff-only FETCH_HEAD", out string mergeOut ) )
		{
			_lastPullWarning = "";
			Git( root, "log -1 --format=%s", out string subject );
			Log.Info( $"AICarTelemetryExporter: pulled {behind.Trim()} new commit(s) on {CodeBranch}: {subject.Trim()}" );
		}
		else if ( mergeOut != _lastPullWarning )
		{
			_lastPullWarning = mergeOut;
			Log.Warning( $"AICarTelemetryExporter: could not fast-forward {CodeBranch} (local changes or diverged history). Resolve it by hand:\n{mergeOut}" );
		}
	}

	// ================================================================== Git

	private static bool Git( string directory, string arguments, out string output )
	{
		var info = new ProcessStartInfo( "git", arguments )
		{
			WorkingDirectory = directory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		info.Environment["GIT_TERMINAL_PROMPT"] = "0";

		try
		{
			using var process = Process.Start( info );
			if ( process is null )
			{
				output = "git did not start";
				return false;
			}

			Task<string> stdout = process.StandardOutput.ReadToEndAsync();
			Task<string> stderr = process.StandardError.ReadToEndAsync();
			if ( !process.WaitForExit( 120_000 ) )
			{
				try { process.Kill( true ); } catch { }
				output = $"git {arguments} timed out";
				return false;
			}

			output = stdout.Result + stderr.Result;
			return process.ExitCode == 0;
		}
		catch ( Exception e )
		{
			output = $"git {arguments}: {e.Message}";
			return false;
		}
	}
}
