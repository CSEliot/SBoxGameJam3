// razorgen — transpile a project's .razor into C# that GameCompileCheck.csproj can compile. Mirrors
// the editor pipeline (Sandbox.Razor.RazorProcessor.GenerateFromSource). Output goes to the in-repo
// CodeTestPortable/gen — a SIBLING of Code/, so the s&box editor (which compiles Code/** only) never
// sees it and there is no SB6001 collision with the editor's own transpile. NEVER write the generated
// .cs under Code/ itself. Stale *.razor.cs are wiped first so a removed/renamed .razor leaves no
// ghost type behind.
//
// Paths are anchored to THIS source file (CallerFilePath), so they survive a project move and don't
// depend on cwd or bin depth, and no project name needs to be known ahead of time. Layout:
// <project>/CodeTestPortable/razorgen/Program.cs ; Code is ../../Code ; gen is ../gen .

using System.Runtime.CompilerServices;
using Sandbox.Razor;

var razorgenDir = ThisDir();
var projCode = Path.GetFullPath( Path.Combine( razorgenDir, "..", "..", "Code" ) );
var genDir = Path.GetFullPath( Path.Combine( razorgenDir, "..", "gen" ) );
Directory.CreateDirectory( genDir );

foreach ( var stale in Directory.GetFiles( genDir, "*.razor.cs", SearchOption.AllDirectories ) )
    File.Delete( stale );

if ( !Directory.Exists( projCode ) )
{
    Console.WriteLine( $"razorgen: no Code/ folder found at {projCode} - nothing to do." );
    return;
}

var razors = Directory.GetFiles( projCode, "*.razor", SearchOption.AllDirectories );
foreach ( var razor in razors )
{
    var text = File.ReadAllText( razor );
    var cs = RazorProcessor.GenerateFromSource( text, razor, "Sandbox" );

    // Flatten the relative path so two same-named components in different folders never collide.
    var rel = Path.GetRelativePath( projCode, razor ).Replace( '\\', '.' ).Replace( '/', '.' );
    var outPath = Path.Combine( genDir, rel + ".cs" );
    File.WriteAllText( outPath, cs );
    Console.WriteLine( $"razorgen: {rel}.cs ({cs.Length} chars)" );
}

Console.WriteLine( $"razorgen: {razors.Length} file(s) -> {genDir}" );

static string ThisDir( [CallerFilePath] string path = "" ) => Path.GetDirectoryName( path );
