using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BannerlordCoopLauncher.Core.Launch;

public static class PlayerSettlementCompatPatch
{
    public const string FileName = "PlayerSettlement.dll";

    public static PatchBatch Apply(IEnumerable<string> roots)
    {
        var batch = new PatchBatch();
        foreach (var file in Find(roots))
        {
            try
            {
                if (PatchAssembly(file) == PatchStatus.Patched)
                    batch.Patched.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                batch.Errors.Add(file + ": " + ex.Message);
            }
        }

        return batch;
    }

    public static PatchStatus PatchAssembly(string path)
    {
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        var replacement = SafeGetTypes(module);
        var changed = false;
        foreach (var method in module.Types.SelectMany(AllMethods))
        {
            if (!method.HasBody)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference called)
                    continue;
                if (!IsAssemblyGetTypes(called))
                    continue;

                instruction.OpCode = OpCodes.Call;
                instruction.Operand = replacement;
                changed = true;
            }
        }

        if (!changed)
            return AlreadyRedirected(module) ? PatchStatus.AlreadyPatched : PatchStatus.Unchanged;

        var temp = path + ".patching";
        module.Write(temp);
        var backup = path + ".pre-gettypes.bak";
        if (!File.Exists(backup))
            File.Copy(path, backup);
        File.SetAttributes(path, FileAttributes.Normal);
        File.Move(temp, path, overwrite: true);
        return PatchStatus.Patched;
    }

    private static bool AlreadyRedirected(ModuleDefinition module) =>
        module.Types
            .SelectMany(AllMethods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Any(instruction => instruction.Operand is MethodReference called
                && called.Name == "GetTypesFromAssembly"
                && called.DeclaringType.FullName == "HarmonyLib.AccessTools");

    private static MethodReference SafeGetTypes(ModuleDefinition module)
    {
        var harmony = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == "0Harmony")
            ?? throw new InvalidOperationException("Player Settlement does not reference Harmony.");
        var accessTools = new TypeReference("HarmonyLib", "AccessTools", module, harmony);
        var systemType = new TypeReference("System", "Type", module, module.TypeSystem.CoreLibrary);
        var assemblyType = new TypeReference("System.Reflection", "Assembly", module, module.TypeSystem.CoreLibrary);
        var method = new MethodReference("GetTypesFromAssembly", new ArrayType(systemType), accessTools)
        {
            HasThis = false
        };
        method.Parameters.Add(new ParameterDefinition(assemblyType));
        return module.ImportReference(method);
    }

    private static bool IsAssemblyGetTypes(MethodReference called) =>
        called.Name == "GetTypes"
        && called.Parameters.Count == 0
        && called.DeclaringType.FullName == "System.Reflection.Assembly";

    private static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type)
    {
        foreach (var method in type.Methods)
            yield return method;
        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in AllMethods(nested))
                yield return method;
        }
    }

    private static IEnumerable<string> Find(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = ClientFiles.Under(root, FileName);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (file.Contains(".pre-gettypes.bak", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(file))
                    yield return file;
            }
        }
    }
}

public enum PatchStatus
{
    Patched,
    AlreadyPatched,
    Unchanged
}

public sealed class PatchBatch
{
    public List<string> Patched { get; } = [];
    public List<string> Errors { get; } = [];
}
