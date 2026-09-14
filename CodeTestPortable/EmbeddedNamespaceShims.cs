// Shim for the engine's source-generated embedded namespace.
// Newer engine builds emit `global using Microsoft.Extensions.Validation.Embedded;`
// into the project's GlobalUsings.g.cs; the real type is source-generated into each
// assembly by the engine compiler, not shipped in a DLL. This empty declaration
// satisfies the global using (CS0234) for the offline gate.
namespace Microsoft.Extensions.Validation.Embedded
{
	internal static class __GateShim { }
}
