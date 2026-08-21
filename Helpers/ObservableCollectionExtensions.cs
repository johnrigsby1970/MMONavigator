using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MMONavigator.Helpers;

public class FastObservableCollection<T> : ObservableCollection<T> {
    private bool _suppressNotification = false;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) {
        if (_suppressNotification)
            return;

        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
        if (!_suppressNotification)
            base.OnPropertyChanged(e);
    }


    public void AddRange(IEnumerable<T> items) {
        if (items == null) return;

        _suppressNotification = true;
        try {
            foreach (var item in items) {
                Add(item);
            }
        }
        finally {
            _suppressNotification = false;
        }

        // Raise single notifications for the whole batch
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    // public void ReplaceRange(IEnumerable<T> items) {
    //     if (items == null) return;
    //
    //     _suppressNotification = true;
    //     try {
    //         Clear();
    //         foreach (var item in items) {
    //             Add(item);
    //         }
    //     }
    //     finally {
    //         _suppressNotification = false;
    //     }
    //
    //     // Raise single notifications for the complete refresh
    //     OnPropertyChanged(new PropertyChangedEventArgs("Count"));
    //     OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    //     OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    // }
    
    public void ReplaceRange(IEnumerable<T> items)
    {
        if (items == null) return;

        // Phase 1: Clear the collection and fire ONE explicit Reset for the teardown
        if (Items.Count > 0)
        {
            _suppressNotification = true;
            try
            {
                Items.Clear();
            }
            finally
            {
                _suppressNotification = false;
            }

            // Fire the teardown notification so dependents can clean up
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // Phase 2: Add all new items completely silently
        if (items.Any())
        {
            _suppressNotification = true;
            try
            {
                foreach (var item in items)
                {
                    // Use Items.Add directly to bypass any derived class overrides
                    Items.Add(item); 
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            // Phase 3: Fire ONE final notification for the complete new state
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}