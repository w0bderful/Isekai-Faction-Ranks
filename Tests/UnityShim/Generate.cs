// Emits [TypeForwardedTo] for every public UnityStubs type so tests can bind
// UnityEngine.* references outside RimWorld 1.7's runtime redirect.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var lines = md.TypeDefinitions.Select(md.GetTypeDefinition)
    .Where(t => (t.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
    .Select(t =>
    {
        string ns = md.GetString(t.Namespace), name = md.GetString(t.Name);
        int tick = name.IndexOf('`');
        string generic = tick < 0 ? "" : "<" + new string(',', int.Parse(name.Substring(tick + 1)) - 1) + ">";
        if (tick >= 0) name = name.Substring(0, tick);
        return "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::" + (ns.Length > 0 ? ns + "." : "") + name + generic + "))]";
    });
File.WriteAllLines(args[1], lines);
