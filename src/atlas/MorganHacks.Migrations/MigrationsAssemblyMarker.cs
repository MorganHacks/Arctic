namespace MorganHacks.Migrations;

/// <summary>
/// Exists so other assemblies can reference this one for script discovery.
/// DbUp finds embedded scripts by assembly, and a top-level Program has no
/// type to point at from outside.
/// </summary>
public sealed class MigrationsAssemblyMarker;
