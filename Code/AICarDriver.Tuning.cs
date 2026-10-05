// Project: sboxgamejam3
// File:    AICarDriver.Tuning.cs
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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Live tuning for the AI car feedback loop, applied from a tick (OnStart does not re-run on
/// hotload). Every call of <see cref="TuningTickHelper"/> compares the applied state against
/// <see cref="AICarDriver.DriverVersion"/> and <see cref="AICarTelemetrySink.TuningVersion"/>; when
/// either moved it restores every property it previously overrode to its captured scene value,
/// then re-applies code tuning (<see cref="BuildCodeTuningHelper"/>) and file tuning from
/// PROJECT/.aicar-telemetry/tuning.json (relayed by the editor exporter into
/// <see cref="AICarTelemetrySink.SetTuning"/>), where the file wins over code on a name clash.
/// While <see cref="UseDevTuning"/> is off nothing is applied and everything stays restored, so
/// the car drives purely on the inspector / scene values.
///
/// Properties are read and written by name through the TypeLibrary (game code may not use
/// System.Reflection); the first time a property is touched its scene value is captured so it can
/// be restored later. Unknown names and bad values log one warning per name and are skipped -
/// tuning never throws and never stops the car from driving.
/// </summary>
public sealed partial class AICarDriver
{
	/// <summary>
	/// Revision label of the currently applied file tuning: the JSON "revision" field, or
	/// $"file-v{TuningVersion}" when the field is missing, or "none" when no file tuning is applied.
	/// Part of the telemetry epoch key ("{DriverVersion}|{TuningRevision}").
	/// </summary>
	public string TuningRevision { get; private set; } = "none";

	// Last state we applied, so a tick that sees no change is just a few comparisons.
	// (Hotload rule: these are default(T) on instances that existed before a hotload, and the
	// "never applied" null sentinel below makes the next tick re-apply - which is the point.)
	private string _tuningLastDriverVersion;
	private int _tuningLastSinkVersion;
	private bool _tuningLastUseDevTuning;

	// name -> original scene value, captured the first time this instance overrides the property.
	private Dictionary<string, object> _tuningOriginals;
	// name -> merged raw value currently in effect (code + file), for the applied-tuning record.
	private Dictionary<string, object> _tuningApplied;
	// names whose override actually landed, in apply order - what gets restored on a re-apply.
	// No field initializer: hotload leaves new fields default(T) on existing instances, so lazy-init.
	private List<string> _tuningAppliedNames;

	// Parsed file tuning. _tuningFileValues is null when no file tuning applies; otherwise it holds
	// the (possibly empty) "values" object and _tuningFileRevision the "revision" field (null = missing).
	private Dictionary<string, object> _tuningFileValues;
	private string _tuningFileRevision;
	private int _tuningFileParsedVersion = -1;

	private static HashSet<string> _tuningWarnedNames;

	/// <summary>Properties that switch the loop itself on/off; the tuning file must not flip them.</summary>
	private static bool IsUntunableHelper( string name ) => name == nameof( UseDevTuning ) || name == nameof( RecordTelemetry );

	/// <summary>
	/// A test-car clone is copied from this car AFTER tuning landed, so its properties already hold
	/// tuned values. Hand it this car's captured scene values and the list of overridden names, so its
	/// first <see cref="TuningTickHelper"/> restores real scene values before applying tuning, and a
	/// later restore (tuning file removed, UseDevTuning off) goes back to the scene, not to old tuning.
	/// Must run before the clone's OnStart (OnStart is deferred to its first update, so right after Clone()).
	/// </summary>
	private void CopyTuningStateToHelper( AICarDriver clone )
	{
		if ( clone is null || _tuningOriginals is null || _tuningAppliedNames is null ) return;
		clone._tuningOriginals = new Dictionary<string, object>( _tuningOriginals );
		clone._tuningAppliedNames = new List<string>( _tuningAppliedNames );
	}

	/// <summary>
	/// Detects a DriverVersion or tuning-file change and (re-)applies tuning. Called from OnStart
	/// (after the IsProxy early-out) and at the top of every host OnFixedUpdate. Returns true when
	/// the applied configuration changed this call (the first call always returns true), which is
	/// the signal for telemetry to open a new epoch.
	/// </summary>
	private bool TuningTickHelper()
	{
		bool changed = _tuningLastDriverVersion is null
			|| _tuningLastDriverVersion != DriverVersion
			|| _tuningLastSinkVersion != AICarTelemetrySink.TuningVersion
			|| _tuningLastUseDevTuning != UseDevTuning;
		if ( !changed ) return false;

		_tuningLastDriverVersion = DriverVersion;
		_tuningLastSinkVersion = AICarTelemetrySink.TuningVersion;
		_tuningLastUseDevTuning = UseDevTuning;

		_tuningOriginals ??= new();
		_tuningAppliedNames ??= new();

		try
		{
			RestoreOverridesHelper();
			ParseFileTuningHelper();

			var values = new Dictionary<string, object>();
			if ( UseDevTuning )
			{
				BuildCodeTuningHelper( values );
				if ( _tuningFileValues is not null )
				{
					// File wins over code on a name clash.
					foreach ( var pair in _tuningFileValues )
						values[pair.Key] = pair.Value;
				}
			}

			ApplyOverridesHelper( values );

			// The label alone is not enough to key an epoch: the agent may tweak values and keep the
			// revision name. Suffix a hash of what actually landed so different values never share
			// a key, while identical values (e.g. a revert) map back to the same key on every car.
			TuningRevision = UseDevTuning && _tuningFileValues is not null
				? $"{_tuningFileRevision ?? $"file-v{_tuningLastSinkVersion}"}#{TuningHashHelper( AppliedTuningJsonHelper() )}"
				: "none";
		}
		catch ( Exception e )
		{
			// Tuning must never stop the car: bail out with everything restored.
			Log.Warning( $"AICarDriver tuning: unexpected failure while applying tuning: {e.Message}" );
			RestoreOverridesHelper();
			_tuningApplied = null;
			TuningRevision = "none";
		}

		return true;
	}

	/// <summary>
	/// Code-side tuning: fills name -> value. Replaces the old ApplyDevTuningHelper. Applied while
	/// <see cref="UseDevTuning"/> is on; the tuning file overrides these on a name clash.
	/// </summary>
	private void BuildCodeTuningHelper( Dictionary<string, object> values )
	{
		// v1-baseline: the scene's own values, plus test cars for more data per play session.
		values["ExtraTestCars"] = 2;
	}

	/// <summary>
	/// JSON object text {"Name": value, ...} of every override currently applied (code + file
	/// merged), "{}" if none. Written into each epoch's summary.json.
	/// </summary>
	private string AppliedTuningJsonHelper()
	{
		if ( _tuningApplied is null || _tuningAppliedNames is null || _tuningAppliedNames.Count == 0 ) return "{}";

		var sb = new StringBuilder( "{ " );
		for ( int i = 0; i < _tuningAppliedNames.Count; i++ )
		{
			string name = _tuningAppliedNames[i];
			if ( i > 0 ) sb.Append( ", " );
			sb.Append( '"' ).Append( name ).Append( "\": " ).Append( TuningValueJsonHelper( _tuningApplied[name] ) );
		}
		sb.Append( " }" );
		return sb.ToString();
	}

	// ================================================================== Internals

	/// <summary>Writes every previously overridden property back to its captured scene value.</summary>
	private void RestoreOverridesHelper()
	{
		if ( _tuningAppliedNames.Count == 0 ) return;

		var td = TypeLibrary.GetType<AICarDriver>();
		foreach ( string name in _tuningAppliedNames )
		{
			// A rename in new code or a missing capture: nothing we can restore, forget the override.
			if ( td is null || !_tuningOriginals.TryGetValue( name, out object original ) ) continue;

			PropertyDescription prop = td.GetProperty( name );
			if ( prop is null || !prop.CanWrite ) continue;

			try
			{
				prop.SetValue( this, original );
			}
			catch ( Exception e )
			{
				WarnTuningHelper( name, $"'{name}' could not be restored to its scene value: {e.Message}" );
			}
		}

		_tuningAppliedNames.Clear();
	}

	/// <summary>
	/// Applies the merged tuning values. Captures each property's original scene value the first
	/// time it is touched; unknown names, read-only properties and unconvertible values are warned
	/// once per name and skipped.
	/// </summary>
	private void ApplyOverridesHelper( Dictionary<string, object> values )
	{
		_tuningApplied = values;
		if ( values.Count == 0 ) return;

		var td = TypeLibrary.GetType<AICarDriver>();
		if ( td is null )
		{
			Log.Warning( "AICarDriver tuning: TypeLibrary has no description for AICarDriver, tuning not applied." );
			return;
		}

		foreach ( var pair in values )
		{
			string name = pair.Key;
			if ( IsUntunableHelper( name ) )
			{
				WarnTuningHelper( name, $"'{name}' controls the loop itself and cannot be tuned (toggle it in the inspector)." );
				continue;
			}

			PropertyDescription prop = td.GetProperty( name );
			if ( prop is null )
			{
				WarnTuningHelper( name, $"'{name}' is not a tunable property of AICarDriver (unknown name)." );
				continue;
			}
			if ( !prop.CanWrite )
			{
				WarnTuningHelper( name, $"'{name}' has no setter, cannot tune." );
				continue;
			}

			if ( !_tuningOriginals.ContainsKey( name ) )
			{
				try
				{
					_tuningOriginals[name] = prop.GetValue( this );
				}
				catch ( Exception e )
				{
					WarnTuningHelper( name, $"'{name}' could not be read: {e.Message}" );
					continue;
				}
			}

			if ( !TryConvertTuningValueHelper( pair.Value, prop.PropertyType, out object converted ) )
			{
				WarnTuningHelper( name, $"value {TuningValueJsonHelper( pair.Value )} does not fit '{name}'." );
				continue;
			}

			try
			{
				prop.SetValue( this, converted );
				_tuningAppliedNames.Add( name );
			}
			catch ( Exception e )
			{
				WarnTuningHelper( name, $"'{name}' could not be set: {e.Message}" );
			}
		}
	}

	/// <summary>
	/// (Re-)parses AICarTelemetrySink.TuningJson whenever the sink version moved. No file, or
	/// malformed/unusable JSON, means no file tuning applies (_tuningFileValues = null).
	/// </summary>
	private void ParseFileTuningHelper()
	{
		if ( _tuningFileParsedVersion == _tuningLastSinkVersion ) return;
		_tuningFileParsedVersion = _tuningLastSinkVersion;
		_tuningFileRevision = null;

		string json = AICarTelemetrySink.TuningJson;
		if ( string.IsNullOrWhiteSpace( json ) )
		{
			_tuningFileValues = null;
			return;
		}

		try
		{
			using var doc = JsonDocument.Parse( json );
			if ( doc.RootElement.ValueKind != JsonValueKind.Object )
			{
				Log.Warning( "AICarDriver tuning: tuning.json root is not a JSON object, file tuning ignored." );
				_tuningFileValues = null;
				return;
			}

			var values = new Dictionary<string, object>();
			if ( doc.RootElement.TryGetProperty( "revision", out var revision ) && revision.ValueKind == JsonValueKind.String )
			{
				// The label ends up in CSV cells and the epoch key: keep it to one clean token.
				string label = revision.GetString()?.Trim()
					.Replace( ',', '_' ).Replace( '"', '_' ).Replace( '|', '_' ).Replace( '#', '_' )
					.Replace( '\n', '_' ).Replace( '\r', '_' );
				_tuningFileRevision = string.IsNullOrEmpty( label ) ? null : label;
			}

			if ( doc.RootElement.TryGetProperty( "values", out var valuesElement ) )
			{
				if ( valuesElement.ValueKind != JsonValueKind.Object )
				{
					Log.Warning( "AICarDriver tuning: tuning.json \"values\" is not an object, file tuning ignored." );
					_tuningFileValues = null;
					return;
				}

				foreach ( var property in valuesElement.EnumerateObject() )
				{
					object value = property.Value.ValueKind switch
					{
						JsonValueKind.Number => property.Value.GetDouble(),
						JsonValueKind.True => true,
						JsonValueKind.False => false,
						JsonValueKind.String => property.Value.GetString(),
						// null / objects / arrays: no scalar value; surfaces as a one-time warning
						// per name in ApplyOverridesHelper.
						_ => null
					};
					values[property.Name] = value;
				}
			}

			// "values" missing entirely = a file that applies zero overrides (e.g. a plain
			// baseline revision) - still valid, the revision label applies.
			_tuningFileValues = values;
		}
		catch ( JsonException e )
		{
			Log.Warning( $"AICarDriver tuning: tuning.json is not valid JSON ({e.Message}), file tuning ignored." );
			_tuningFileValues = null;
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarDriver tuning: could not read tuning.json ({e.Message}), file tuning ignored." );
			_tuningFileValues = null;
		}
	}

	/// <summary>
	/// Converts a JSON scalar (double/bool/string, or a raw code-tuning value) to the property's
	/// type: float/int/bool/string, the only kinds tuning uses. Anything else is not tunable.
	/// Never throws.
	/// </summary>
	private static bool TryConvertTuningValueHelper( object value, Type targetType, out object converted )
	{
		converted = null;
		if ( value is null || targetType is null ) return false;

		if ( targetType == typeof( float ) )
		{
			switch ( value )
			{
				case float f: converted = f; return true;
				case double d: converted = (float)d; return true;
				case int i: converted = (float)i; return true;
				case string s when float.TryParse( s, CultureInfo.InvariantCulture, out float p ): converted = p; return true;
			}
			return false;
		}

		if ( targetType == typeof( int ) )
		{
			switch ( value )
			{
				case int i: converted = i; return true;
				case double d: converted = (int)Math.Round( d ); return true;
				case float f: converted = (int)Math.Round( f ); return true;
				case string s when int.TryParse( s, CultureInfo.InvariantCulture, out int p ): converted = p; return true;
			}
			return false;
		}

		if ( targetType == typeof( bool ) )
		{
			switch ( value )
			{
				case bool b: converted = b; return true;
				case double d: converted = d != 0.0; return true;
				case int i: converted = i != 0; return true;
				case string s:
					if ( bool.TryParse( s, out bool p ) ) { converted = p; return true; }
					if ( s == "1" ) { converted = true; return true; }
					if ( s == "0" ) { converted = false; return true; }
					return false;
			}
			return false;
		}

		if ( targetType == typeof( string ) )
		{
			converted = value as string ?? value.ToString();
			return true;
		}

		return false;
	}

	/// <summary>One Log.Warning per property name, ever, so a bad tuning file cannot spam the console.</summary>
	private static void WarnTuningHelper( string name, string message )
	{
		_tuningWarnedNames ??= new();
		if ( !_tuningWarnedNames.Add( name ) ) return;
		Log.Warning( $"AICarDriver tuning: {message}" );
	}

	/// <summary>A raw tuning value back to JSON scalar text for the applied-tuning record.</summary>
	private static string TuningValueJsonHelper( object value ) => value switch
	{
		null => "null",
		bool b => b ? "true" : "false",
		string s => TuningQuoteHelper( s ),
		double d => double.IsFinite( d ) ? d.ToString( "0.####", CultureInfo.InvariantCulture ) : "null",
		float f => float.IsFinite( f ) ? f.ToString( "0.####", CultureInfo.InvariantCulture ) : "null",
		int i => i.ToString( CultureInfo.InvariantCulture ),
		_ => TuningQuoteHelper( value.ToString() )
	};

	/// <summary>
	/// 6-hex-digit FNV-1a hash. Deterministic across cars, hotloads and sessions (string.GetHashCode
	/// is randomized per process), so the same applied values always give the same epoch key.
	/// </summary>
	private static string TuningHashHelper( string text )
	{
		uint hash = 2166136261;
		foreach ( char c in text ?? "" )
		{
			hash ^= c;
			hash *= 16777619;
		}
		return (hash & 0xFFFFFF).ToString( "x6", CultureInfo.InvariantCulture );
	}

	private static string TuningQuoteHelper( string value )
	{
		var sb = new StringBuilder( value.Length + 2 );
		sb.Append( '"' );
		foreach ( char c in value )
		{
			switch ( c )
			{
				case '"': sb.Append( "\\\"" ); break;
				case '\\': sb.Append( "\\\\" ); break;
				case '\n': sb.Append( "\\n" ); break;
				case '\r': break;
				case '\t': sb.Append( ' ' ); break;
				default:
					if ( c < ' ' ) break;
					sb.Append( c );
					break;
			}
		}
		sb.Append( '"' );
		return sb.ToString();
	}
}
