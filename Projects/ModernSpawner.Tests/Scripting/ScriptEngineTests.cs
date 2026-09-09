using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Scripting.Ast;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Scripting;

public class ScriptEngineTests
{
    private readonly ScriptEngine _engine = new();

    #region Basic Compilation Tests

    [Fact]
    public void Compile_NullScript_ReturnsEmpty()
    {
        var result = _engine.Compile(null);

        Assert.Same(CompiledScript.Empty, result);
    }

    [Fact]
    public void Compile_EmptyScript_ReturnsEmpty()
    {
        var result = _engine.Compile("");

        Assert.Same(CompiledScript.Empty, result);
    }

    [Fact]
    public void Compile_WhitespaceScript_ReturnsEmpty()
    {
        var result = _engine.Compile("   \t\n   ");

        Assert.Same(CompiledScript.Empty, result);
    }

    [Fact]
    public void Compile_ValidScript_ReturnsValidCompiledScript()
    {
        var result = _engine.Compile("SET/Name/Test");

        Assert.True(result.IsValid);
        Assert.NotNull(result.Root);
    }

    #endregion

    #region Command Parsing Tests

    [Fact]
    public void Compile_CancelCommand_CreatesCancelNode()
    {
        var result = _engine.Compile("CANCEL");

        Assert.True(result.IsValid);
        Assert.IsType<CancelSpawnNode>(result.Root);
    }

    [Fact]
    public void Compile_SetCommand_CreatesSetPropertyNode()
    {
        var result = _engine.Compile("SET/Name/Test Value");

        Assert.True(result.IsValid);
        Assert.IsType<SetPropertyNode>(result.Root);
    }

    [Fact]
    public void Compile_SetVarCommand_CreatesSetVariableNode()
    {
        var result = _engine.Compile("SETVAR/myvar/123");

        Assert.True(result.IsValid);
        Assert.IsType<SetVariableNode>(result.Root);
    }

    [Fact]
    public void Compile_MsgCommand_CreatesSendMessageNode()
    {
        var result = _engine.Compile("MSG/Hello World");

        Assert.True(result.IsValid);
        Assert.IsType<SendMessageNode>(result.Root);

        var node = (SendMessageNode)result.Root;
        Assert.Equal("Hello World", node.Message);
    }

    [Fact]
    public void Compile_UnknownCommand_ReturnsInvalid()
    {
        var result = _engine.Compile("UNKNOWN/arg");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.CompileErrors);
    }

    #endregion

    #region Inter-Spawner Command Tests

    [Fact]
    public void Compile_SpawnCommand_CreatesSpawnCommandNode()
    {
        var result = _engine.Compile("SPAWN/spawnerName");

        Assert.True(result.IsValid);
        Assert.IsType<SpawnCommandNode>(result.Root);
    }

    [Fact]
    public void Compile_SpawnCommand_WithEntryIndex()
    {
        var result = _engine.Compile("SPAWN/spawnerName/2");

        Assert.True(result.IsValid);
        Assert.IsType<SpawnCommandNode>(result.Root);

        var node = (SpawnCommandNode)result.Root;
        Assert.Equal("spawnerName", node.SpawnerIdentifier);
        Assert.Equal(2, node.EntryIndex);
    }

    [Fact]
    public void Compile_DespawnCommand_CreatesDespawnCommandNode()
    {
        var result = _engine.Compile("DESPAWN/spawnerName");

        Assert.True(result.IsValid);
        Assert.IsType<DespawnCommandNode>(result.Root);
    }

    [Fact]
    public void Compile_DespawnCommand_WithEntryAndCount()
    {
        var result = _engine.Compile("DESPAWN/spawnerName/1/5");

        Assert.True(result.IsValid);
        Assert.IsType<DespawnCommandNode>(result.Root);

        var node = (DespawnCommandNode)result.Root;
        Assert.Equal("spawnerName", node.SpawnerIdentifier);
        Assert.Equal(1, node.EntryIndex);
        Assert.Equal(5, node.Count);
    }

    [Fact]
    public void Compile_GotoCommand_CreatesGotoCommandNode()
    {
        var result = _engine.Compile("GOTO/100,200,0");

        Assert.True(result.IsValid);
        Assert.IsType<GotoCommandNode>(result.Root);
    }

    [Fact]
    public void Compile_ActivateCommand_CreatesActivateSpawnerNode()
    {
        var result = _engine.Compile("ACTIVATE/spawnerName");

        Assert.True(result.IsValid);
        Assert.IsType<ActivateSpawnerNode>(result.Root);

        var node = (ActivateSpawnerNode)result.Root;
        Assert.True(node.Activate);
    }

    [Fact]
    public void Compile_DeactivateCommand_CreatesActivateSpawnerNode_WithActivateFalse()
    {
        var result = _engine.Compile("DEACTIVATE/spawnerName");

        Assert.True(result.IsValid);
        Assert.IsType<ActivateSpawnerNode>(result.Root);

        var node = (ActivateSpawnerNode)result.Root;
        Assert.False(node.Activate);
    }

    [Fact]
    public void Compile_BroadcastCommand_CreatesBroadcastNode()
    {
        var result = _engine.Compile("BROADCAST/Hello everyone");

        Assert.True(result.IsValid);
        Assert.IsType<BroadcastNode>(result.Root);

        var node = (BroadcastNode)result.Root;
        Assert.Equal("Hello everyone", node.Message);
    }

    [Fact]
    public void Compile_BroadcastCommand_WithRangeAndHue()
    {
        var result = _engine.Compile("BROADCAST/Warning/20/0x22");

        Assert.True(result.IsValid);
        Assert.IsType<BroadcastNode>(result.Root);

        var node = (BroadcastNode)result.Root;
        Assert.Equal("Warning", node.Message);
        Assert.Equal(20, node.Range);
    }

    [Fact]
    public void Compile_SoundCommand_CreatesPlaySoundNode()
    {
        var result = _engine.Compile("SOUND/500");

        Assert.True(result.IsValid);
        Assert.IsType<PlaySoundNode>(result.Root);

        var node = (PlaySoundNode)result.Root;
        Assert.Equal(500, node.SoundId);
    }

    [Fact]
    public void Compile_EffectCommand_CreatesPlayEffectNode()
    {
        var result = _engine.Compile("EFFECT/14000");

        Assert.True(result.IsValid);
        Assert.IsType<PlayEffectNode>(result.Root);

        var node = (PlayEffectNode)result.Root;
        Assert.Equal(14000, node.EffectId);
    }

    #endregion

    #region Multi-Command Tests

    [Fact]
    public void Compile_MultipleCommands_Semicolon_CreatesSequenceNode()
    {
        var result = _engine.Compile("SET/Name/Test;SET/Hits/100");

        Assert.True(result.IsValid);
        Assert.IsType<SequenceNode>(result.Root);

        var sequence = (SequenceNode)result.Root;
        Assert.Equal(2, sequence.Children.Count);
    }

    [Fact]
    public void Compile_MultipleCommands_Newline_CreatesSequenceNode()
    {
        var result = _engine.Compile("SET/Name/Test\nSET/Hits/100");

        Assert.True(result.IsValid);
        Assert.IsType<SequenceNode>(result.Root);

        var sequence = (SequenceNode)result.Root;
        Assert.Equal(2, sequence.Children.Count);
    }

    [Fact]
    public void Compile_MultipleCommands_MixedSeparators()
    {
        var result = _engine.Compile("SET/Name/Test;SET/Hits/100\nCANCEL");

        Assert.True(result.IsValid);
        Assert.IsType<SequenceNode>(result.Root);

        var sequence = (SequenceNode)result.Root;
        Assert.Equal(3, sequence.Children.Count);
    }

    [Fact]
    public void Compile_SingleCommand_NoSequenceNode()
    {
        var result = _engine.Compile("CANCEL");

        Assert.True(result.IsValid);
        Assert.IsType<CancelSpawnNode>(result.Root); // Not wrapped in SequenceNode
    }

    [Fact]
    public void Compile_EmptyCommands_Filtered()
    {
        var result = _engine.Compile("SET/Name/Test;;;\n\nCANCEL");

        Assert.True(result.IsValid);
        Assert.IsType<SequenceNode>(result.Root);

        var sequence = (SequenceNode)result.Root;
        Assert.Equal(2, sequence.Children.Count);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Compile_SetWithoutValue_ReturnsError()
    {
        var result = _engine.Compile("SET");

        Assert.False(result.IsValid);
        Assert.Contains(result.CompileErrors, e => e.Contains("SET command requires"));
    }

    [Fact]
    public void Compile_SetWithoutPropertyValue_ReturnsError()
    {
        var result = _engine.Compile("SET/PropertyOnly");

        Assert.False(result.IsValid);
        Assert.Contains(result.CompileErrors, e => e.Contains("SET command requires"));
    }

    [Fact]
    public void Compile_SpawnWithoutIdentifier_ReturnsError()
    {
        var result = _engine.Compile("SPAWN");

        Assert.False(result.IsValid);
        Assert.Contains(result.CompileErrors, e => e.Contains("SPAWN command requires"));
    }

    [Fact]
    public void Compile_SoundWithNonNumericId_ReturnsError()
    {
        var result = _engine.Compile("SOUND/notanumber");

        Assert.False(result.IsValid);
        Assert.Contains(result.CompileErrors, e => e.Contains("numeric sound ID"));
    }

    [Fact]
    public void Compile_EffectWithNonNumericId_ReturnsError()
    {
        var result = _engine.Compile("EFFECT/notanumber");

        Assert.False(result.IsValid);
        Assert.Contains(result.CompileErrors, e => e.Contains("numeric effect ID"));
    }

    #endregion

    #region Caching Tests

    [Fact]
    public void Compile_SameScript_ReturnsCached()
    {
        var result1 = _engine.Compile("SET/Name/Test");
        var result2 = _engine.Compile("SET/Name/Test");

        Assert.Same(result1, result2);
    }

    [Fact]
    public void Compile_DifferentScript_ReturnsNew()
    {
        var result1 = _engine.Compile("SET/Name/Test1");
        var result2 = _engine.Compile("SET/Name/Test2");

        Assert.NotSame(result1, result2);
    }

    [Fact]
    public void ClearCache_InvalidatesCachedScripts()
    {
        var result1 = _engine.Compile("SET/Name/Test");
        _engine.ClearCache();
        var result2 = _engine.Compile("SET/Name/Test");

        Assert.NotSame(result1, result2);
    }

    #endregion

    #region Case Insensitivity Tests

    [Theory]
    [InlineData("SET/Name/Test")]
    [InlineData("set/Name/Test")]
    [InlineData("Set/Name/Test")]
    [InlineData("sEt/Name/Test")]
    public void Compile_CommandsAreCaseInsensitive(string script)
    {
        var result = _engine.Compile(script);

        Assert.True(result.IsValid);
        Assert.IsType<SetPropertyNode>(result.Root);
    }

    [Theory]
    [InlineData("CANCEL")]
    [InlineData("cancel")]
    [InlineData("Cancel")]
    public void Compile_Cancel_CaseInsensitive(string script)
    {
        var result = _engine.Compile(script);

        Assert.True(result.IsValid);
        Assert.IsType<CancelSpawnNode>(result.Root);
    }

    #endregion
}
