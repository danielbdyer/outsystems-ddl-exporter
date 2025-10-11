using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Mediation;
using Xunit;

namespace Osm.Pipeline.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_InvokesRegisteredHandler()
    {
        var handler = new RecordingHandler();
        var services = new Dictionary<Type, object>
        {
            { typeof(ICommandHandler<TestCommand, string>), handler }
        };

        var dispatcher = new CommandDispatcher(new DictionaryServiceProvider(services));
        var command = new TestCommand("hello");

        var result = await dispatcher.DispatchAsync<TestCommand, string>(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", handler.ObservedValue);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenHandlerMissing()
    {
        var dispatcher = new CommandDispatcher(new DictionaryServiceProvider(new Dictionary<Type, object>()));

        var command = new TestCommand("unused");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync<TestCommand, string>(command));
    }

    private sealed record TestCommand(string Value) : ICommand<string>;

    private sealed class RecordingHandler : ICommandHandler<TestCommand, string>
    {
        public string? ObservedValue { get; private set; }

        public Task<Result<string>> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            ObservedValue = command.Value;
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class DictionaryServiceProvider : IServiceProvider
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        public DictionaryServiceProvider(IReadOnlyDictionary<Type, object> services)
        {
            _services = services;
        }

        public object? GetService(Type serviceType)
        {
            _services.TryGetValue(serviceType, out var service);
            return service;
        }
    }
}
