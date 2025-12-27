using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SourceGeneration.ChangeTracking;

public interface IChangeTrackingListQuery<TCollection, TItem> :
    ICascadingChangeTracking, IDisposable, IEnumerable<TItem>
    where TCollection : IEnumerable<TItem>, INotifyCollectionChanged
{

}
internal class ChangeTrackingListQuery<TCollection, TItem> : IChangeTrackingListQuery<TCollection,TItem>
    where TCollection : IEnumerable<TItem>, INotifyCollectionChanged
{
    private readonly Func<TItem, bool> _predicate;
    private readonly TCollection _collection;

    private bool _baseChanged;
    private bool _cascadingChanged;
    public bool IsCascadingChanged => _cascadingChanged;
    public bool IsBaseChanged => _baseChanged;
    public bool IsChanged => _baseChanged || _cascadingChanged;

    public void AcceptChanges()
    {
        _cascadingChanged = false;
        _baseChanged = false;
    }

    public IEnumerator<TItem> GetEnumerator() => _collection.Where(_predicate).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _collection.Where(_predicate).GetEnumerator();

    public void Dispose()
    {
        _collection.CollectionChanged -= OnCollectionChanged;

        if (_collection is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    public ChangeTrackingListQuery(TCollection collection, Func<TItem, bool> predicate, bool cascading)
    {
        _predicate = predicate;
        _collection = collection;

        _collection.CollectionChanged += OnCollectionChanged;

        if (cascading && _collection is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_cascadingChanged)
            return;

        if (sender is TItem item && _predicate(item))
        {
            _cascadingChanged = true;
        }
    }

    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_baseChanged)
            return;

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            _baseChanged = HasChange(e.NewItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            _baseChanged = HasChange(e.OldItems);

        }
        else if (e.Action == NotifyCollectionChangedAction.Reset && e.OldItems != null)
        {
            _baseChanged = HasChange(e.OldItems);
        }
        else if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            if (e.OldItems != null)
            {
                _baseChanged = HasChange(e.OldItems);
                if (_baseChanged)
                    return;
            }

            if (e.NewItems != null)
            {
                _baseChanged = HasChange(e.NewItems);
            }
        }

        bool HasChange(IList changeItems)
        {
            foreach (var item in changeItems)
            {
                if (item is TItem t)
                {
                    if (_predicate(t))
                    {
                        return true;
                    }
                }
                else if (item is IEnumerable<TItem> items)
                {
                    if (items.Any(_predicate))
                    {
                        return true;
                    }
                }
            }
            return false;

        }
    }

}
