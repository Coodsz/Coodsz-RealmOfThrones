using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BannerlordCoopLauncher.Core.Launch;

public static class CoopPasswordPatch
{
    public const string FileName = "Coop.Core.dll";
    public const string VariableName = "COODSZ_COOP_PASSWORD";
    public const string HelperName = "PasswordOrToken";

    /// <summary>Coop 0.1.8 and newer (official or nightly) are never rewritten: Coodsz Join Password fills the password
    /// at run time, and Cecil can't write the nightly's Coop.Core.dll without its other files.</summary>
    public const string NewCoopSkipped = "Coop 0.1.8 or newer is left as it is. Coodsz Join Password sends the server password when you connect.";

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ResolutionException)
            {
                batch.Errors.Add(file + ": " + ex.Message);
            }
        }

        return batch;
    }

    public static PatchStatus PatchAssembly(string path)
    {
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        var client = module.Types.FirstOrDefault(type => type.FullName == "Coop.Core.Client.CoopClient");
        var start = client?.Methods.FirstOrDefault(method => method.Name == "Start" && method.HasBody);
        if (start == null)
            return PatchStatus.Unchanged;

        var connect = start.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Operand is MethodReference called
            && called.Name == "Connect"
            && called.DeclaringType.Name == "NetManager");
        if (connect == null)
            return PatchStatus.Unchanged;

        var token = PreviousCall(start, connect);
        if (token?.Operand is MethodReference existing && existing.Name == HelperName)
            return PatchStatus.AlreadyPatched;
        if (token?.Operand is not MethodReference getToken || getToken.Name != "get_Token")
            return PatchStatus.Unchanged;

        var helper = EnsureHelper(module, getToken);
        token.OpCode = OpCodes.Call;
        token.Operand = helper;

        var temp = path + ".patching";
        try
        {
            module.Write(temp);
            // The file is unpatched here, so it is the current original. A backup
            // left by an older Coop (0.1.5 next to a Steam-updated 0.1.6) is replaced.
            var backup = path + ".pre-password.bak";
            File.Copy(path, backup, overwrite: true);
            File.SetAttributes(path, FileAttributes.Normal);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Cecil stops half way when it can't find a file the build needs (GameInterface for the Coop nightly), or the
            // backup or the swap fails (a read only .bak, a locked dll), so the copy goes and Coop.Core.dll stays as it
            // was. A copy that can't go never hides why the patch failed.
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
        return PatchStatus.Patched;
    }

    private static Instruction? PreviousCall(MethodDefinition method, Instruction target)
    {
        Instruction? previous = null;
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction == target)
                return previous;
            if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                previous = instruction;
        }

        return null;
    }

    private static MethodDefinition EnsureHelper(ModuleDefinition module, MethodReference getToken)
    {
        var existing = module.Types
            .SelectMany(type => type.Methods)
            .FirstOrDefault(method => method.Name == HelperName && method.IsStatic);
        if (existing != null)
            return existing;

        var helperType = new TypeDefinition(
            "Coop.Core.Client",
            "CoodszPassword",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        var method = new MethodDefinition(
            HelperName,
            MethodAttributes.Public | MethodAttributes.Static,
            module.TypeSystem.String);
        method.Parameters.Add(new ParameterDefinition(getToken.DeclaringType));
        helperType.Methods.Add(method);
        module.Types.Add(helperType);

        var environment = new TypeReference("System", "Environment", module, module.TypeSystem.CoreLibrary);
        var getVariable = new MethodReference("GetEnvironmentVariable", module.TypeSystem.String, environment)
        {
            Parameters = { new ParameterDefinition(module.TypeSystem.String) }
        };
        var isNullOrEmpty = new MethodReference("IsNullOrEmpty", module.TypeSystem.Boolean, module.TypeSystem.String)
        {
            Parameters = { new ParameterDefinition(module.TypeSystem.String) }
        };

        var token = new VariableDefinition(module.TypeSystem.String);
        method.Body.Variables.Add(token);
        method.Body.InitLocals = true;
        var il = method.Body.GetILProcessor();
        var readEnvironment = il.Create(OpCodes.Ldstr, VariableName);
        var done = il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getToken));
        il.Append(il.Create(OpCodes.Stloc, token));
        il.Append(il.Create(OpCodes.Ldloc, token));
        il.Append(il.Create(OpCodes.Call, isNullOrEmpty));
        il.Append(il.Create(OpCodes.Brtrue, readEnvironment));
        il.Append(il.Create(OpCodes.Ldloc, token));
        il.Append(il.Create(OpCodes.Br, done));
        il.Append(readEnvironment);
        il.Append(il.Create(OpCodes.Call, getVariable));
        il.Append(il.Create(OpCodes.Dup));
        il.Append(il.Create(OpCodes.Brtrue, done));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ldstr, ""));
        il.Append(done);
        return method;
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (file.Contains(".pre-password.bak", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(file))
                    yield return file;
            }
        }
    }
}
