using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SourceGeneration.ChangeTracking;

public class ChangeTracker<TState> : IChangeTracker<TState> where TState : class
{
    private ImmutableArray<IChangeTracking> _watches = [];
    private ImmutableArray<Action<TState>> _subscribers = [];
    private readonly Action<ChangeTracker<TState>>? _disposeAction;

    public ChangeTracker(TState state)
    {
        State = state;
    }

    internal ChangeTracker(TState state, Action<ChangeTracker<TState>> disposeAction)
    {
        State = state;
        _disposeAction = disposeAction;
    }

    public TState State { get; }

    public void AcceptChanges()
    {
        lock (State)
        {
            var watches = _watches;
            foreach (var observer in watches.Cast<IObserver<TState>>())
            {
                observer.OnNext(State);
            }

            if (watches.Any(x => x.IsChanged))
            {
                var subscribes = _subscribers;
                foreach (var subscriber in subscribes)
                {
                    subscriber(State);
                }
            }

            foreach (IChangeTracking observer in watches)
            {
                observer.AcceptChanges();
            }
        }
    }

    public IDisposable Watch<TValue>(Func<TState, TValue> selector, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
    {
        return Watch(selector, null, null, scope);
    }

    public IDisposable Watch<TValue>(Func<TState, TValue> selector, Action<TValue>? subscriber, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
    {
        return Watch(selector, null, subscriber, scope);
    }

    public IDisposable Watch<TValue>(Func<TState, TValue> selector, Func<TValue, bool>? predicate, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
    {
        return Watch(selector, predicate, null, scope);
    }

    public IDisposable Watch<TValue>(Func<TState, TValue> selector, Func<TValue, bool>? predicate, Action<TValue>? subscriber, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
    {
        var subscription = new Subscription<TValue>(State, selector, predicate, subscriber, new ChangeTrackingScopeEqualityComparer<TValue>(scope));
        _watches = _watches.Add(subscription);
        return new Disposable(() => _watches = _watches.Remove(subscription));
    }

    public IDisposable Watch<TItem>(Func<TState, ChangeTrackingList<TItem>> selector, Func<TItem, bool> predicate, Action<IEnumerable<TItem>>? subscriber = null, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
    {
        var subscription = new CollectionSubscription<ChangeTrackingList<TItem>, TItem>(State, selector, predicate, subscriber, scope);

        _watches = _watches.Add(subscription);

        return new Disposable(() =>
        {
            subscription.Dispose();
            _watches = _watches.Remove(subscription);
        });

    }


    public IDisposable Watch<TCollection, TItem>(Func<TState, TCollection> selector, Func<TItem, bool> predicate, Action<IEnumerable<TItem>>? subscriber = null, ChangeTrackingScope scope = ChangeTrackingScope.InstanceProperty)
        where TCollection : IEnumerable<TItem>, INotifyCollectionChanged
    {
        var subscription = new CollectionSubscription<TCollection, TItem>(State, selector, predicate, subscriber, scope);

        _watches = _watches.Add(subscription);

        return new Disposable(() =>
        {
            subscription.Dispose();
            _watches = _watches.Remove(subscription);
        });
    }

    public IDisposable OnChange(Action subscriber) => OnChange(_ => subscriber());

    public IDisposable OnChange(Action<TState> subscriber)
    {
        _subscribers = _subscribers.Add(subscriber);
        return new Disposable(() => _subscribers = _subscribers.Remove(subscriber));
    }

    public IDisposable OnChange(int index, Action<TState> subscriber)
    {
        if (index >= _subscribers.Length)
            return OnChange(subscriber);

        if (index < 0) index = 0;
        _subscribers = _subscribers.Insert(index, subscriber);
        return new Disposable(() => _subscribers = _subscribers.Remove(subscriber));
    }

    public void Dispose()
    {
        _subscribers = _subscribers.Clear();
        _watches = _watches.Clear();
        _disposeAction?.Invoke(this);
    }

    private sealed class CollectionSubscription<TCollection, TItem> : IObserver<TState>, IChangeTracking, IDisposable
         where TCollection : IEnumerable<TItem>, INotifyCollectionChanged
    {
        private static readonly IEqualityComparer<TCollection> _equalityComparer = new ChangeTrackingScopeEqualityComparer<TCollection>(ChangeTrackingScope.Instance);
        private readonly Func<TState, TCollection> _selector;
        private readonly Func<TItem, bool> _predicate;
        private readonly Action<IEnumerable<TItem>>? _subscriber;
        private readonly ChangeTrackingScope _scope;

        private TCollection _value = default!;
        private bool _changed;
        private InnerSubscription? _innerSubscription;

        public CollectionSubscription(TState state,
            Func<TState, TCollection> selector,
            Func<TItem, bool> predicate,
            Action<IEnumerable<TItem>>? subscriber,
            ChangeTrackingScope scope)
        {
            _selector = selector;
            _predicate = predicate;
            _subscriber = subscriber;
            _scope = scope;

            _value = _selector(state);

            if (_value != null)
            {
                _innerSubscription = new InnerSubscription(_value, _predicate, _subscriber, _scope);
            }
        }

        public bool IsChanged => _changed || (_innerSubscription?.IsChanged ?? false);

        public void AcceptChanges()
        {
            _changed = false;
            _innerSubscription?.AcceptChanges();
        }

        public void Dispose() => _innerSubscription?.Dispose();

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(TState value)
        {
            var select = _selector(value);

            if (!_equalityComparer.Equals(_value, select))
            {
                _value = select;
                _changed = true;
                if (_innerSubscription != null)
                {
                    _innerSubscription.Dispose();
                    _innerSubscription = null;
                }
                if (_value != null)
                {
                    _innerSubscription = new InnerSubscription(_value, _predicate, _subscriber, _scope);
                }
            }
            else
            {
                _innerSubscription?.OnNext(value);
            }
        }

        private sealed class InnerSubscription : IObserver<TState>, IChangeTracking, IDisposable
        {
            private readonly ChangeTrackingListQuery<TCollection, TItem> _query;
            private readonly Action<IEnumerable<TItem>>? _subscriber;
            private readonly ChangeTrackingScope _scope;
            private bool _changed;

            public InnerSubscription(
                TCollection value,
                Func<TItem, bool> predicate,
                Action<IEnumerable<TItem>>? subscriber,
                ChangeTrackingScope scope)
            {
                _query = new ChangeTrackingListQuery<TCollection, TItem>(value, predicate);
                _subscriber = subscriber;
                _scope = scope;
                _subscriber?.Invoke(_query);
            }

            public bool IsChanged => _changed;

            public void OnNext(TState value)
            {
                _changed = _query.IsChanged;

                if (!_changed)
                    return;

                bool push;

                if (_scope == ChangeTrackingScope.Always)
                {
                    push = true;
                }
                else if (_scope == ChangeTrackingScope.InstanceProperty)
                {
                    push = _query.IsBaseChanged && !_query.IsCascadingChanged;
                }
                else
                {
                    push = _query.IsChanged;
                }

                if (push)
                {
                    _subscriber?.Invoke(_query);
                }
            }

            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void AcceptChanges()
            {
                if (_changed)
                {
                    _query.AcceptChanges();
                    _changed = false;
                }
            }

            public void Dispose() => _query.Dispose();
        }

    }

    private sealed class Subscription<TValue> : IObserver<TState>, IChangeTracking
    {
        private readonly Func<TState, TValue> _selector;
        private readonly Func<TValue, bool>? _predicate;
        private readonly Action<TValue>? _subscriber;
        private readonly IEqualityComparer<TValue> _equalityComparer;
        private TValue _value = default!;
        private bool _changed;

        public Subscription(
            TState state,
            Func<TState, TValue> selector,
            Func<TValue, bool>? predicate,
            Action<TValue>? subscriber,
            IEqualityComparer<TValue> equalityComparer)
        {
            _selector = selector;
            _predicate = predicate;
            _subscriber = subscriber;
            _equalityComparer = equalityComparer;

            if (SetValue(state))
            {
                subscriber?.Invoke(_value);
            }
        }

        public bool IsChanged => _changed;
        public void AcceptChanges() => _changed = false;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(TState value)
        {
            if (SetValue(value))
            {
                _changed = true;
                _subscriber?.Invoke(_value);
            }
        }

        private bool SetValue(TState value)
        {
            var select = _selector(value);

            if (_predicate == null || _predicate(select))
            {
                if (!_equalityComparer.Equals(_value, select))
                {
                    _value = select;
                    return true;
                }
            }
            return false;
        }
    }

    private sealed class Disposable(Action callback) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                callback();
                _disposed = true;
            }
        }
    }
}
