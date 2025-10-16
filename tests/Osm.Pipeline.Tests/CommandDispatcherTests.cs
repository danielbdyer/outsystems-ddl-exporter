using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
        var scopeFactory = new FakeScopeFactory(() => new FakeScope(new FakeServiceProvider(new Dictionary<Type, object>
        {
            { typeof(ICommandHandler<TestCommand, string>), handler }
        })));

        var dispatcher = new CommandDispatcher(scopeFactory);
        var command = new TestCommand("hello");

        var result = await dispatcher.DispatchAsync<TestCommand, string>(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", handler.ObservedValue);
        Assert.Equal("hello", result.Value);
        var scope = scopeFactory.LastCreatedScope;
        Assert.NotNull(scope);
        Assert.True(scope!.IsDisposed);
    }

    [Fact]
    public async Task DispatchAsync_ThrowsWhenHandlerMissing()
    {
        var scopeFactory = new FakeScopeFactory(() => new FakeScope(new FakeServiceProvider(new Dictionary<Type, object>())));
        var dispatcher = new CommandDispatcher(scopeFactory);

        var command = new TestCommand("unused");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync<TestCommand, string>(command));

        var scope = scopeFactory.LastCreatedScope;
        Assert.NotNull(scope);
        Assert.True(scope!.IsDisposed);
    }

    [Fact]
    public async Task DispatchAsync_DisposesHandlerScope()
    {
        var handler = new DisposableRecordingHandler();
        var scopeFactory = new FakeScopeFactory(() => new FakeScope(new FakeServiceProvider(new Dictionary<Type, object>
        {
            { typeof(ICommandHandler<TestCommand, string>), handler }
        })));
        var dispatcher = new CommandDispatcher(scopeFactory);

        await dispatcher.DispatchAsync<TestCommand, string>(new TestCommand("value"));

        var scope = scopeFactory.LastCreatedScope;
        Assert.NotNull(scope);
        Assert.True(scope!.IsDisposed);
        Assert.True(handler.IsDisposed);
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

    private sealed class DisposableRecordingHandler : ICommandHandler<TestCommand, string>, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public string? ObservedValue { get; private set; }

        public Task<Result<string>> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            ObservedValue = command.Value;
            return Task.FromResult(Result<string>.Success(command.Value));
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly Func<FakeScope> _scopeFactory;

        public FakeScopeFactory(Func<FakeScope> scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public FakeScope? LastCreatedScope { get; private set; }

        public IServiceScope CreateScope()
        {
            var scope = _scopeFactory();
            LastCreatedScope = scope;
            return scope;
        }
    }

    private sealed class FakeScope : IServiceScope
    {
        private readonly FakeServiceProvider _serviceProvider;

        public FakeScope(FakeServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider => _serviceProvider;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            _serviceProvider.Dispose();
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider, IDisposable
    {
        private readonly IReadOnlyDictionary<Type, object> _services;
        private bool _disposed;

        public FakeServiceProvider(IReadOnlyDictionary<Type, object> services)
        {
            _services = services;
        }

        public object? GetService(Type serviceType)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FakeServiceProvider));
            }

            _services.TryGetValue(serviceType, out var service);
            return service;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var service in _services.Values)
            {
                if (service is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
