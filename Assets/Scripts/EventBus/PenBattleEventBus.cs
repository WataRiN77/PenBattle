using System;
using System.Collections.Generic;
using System.Linq;

namespace PenBattleGame.Core
{
    public abstract class GameEvent { }

    public class EventBus
    {
        private static readonly Lazy<EventBus> _instance
                          = new Lazy<EventBus>(() => new EventBus());
        public static EventBus Instance => _instance.Value;

        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
        private EventBus() { }

        public void Subscribe<T>(Action<T> handler) where T : GameEvent
        {
            var type = typeof(T);
            if (!_subscribers.ContainsKey(type))
                _subscribers[type] = new List<Delegate>();
            _subscribers[type].Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : GameEvent
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
                list.Remove(handler);
        }

        public void Publish<T>(T evt) where T : GameEvent
        {
            var type = typeof(T);
            if(_subscribers.TryGetValue(type, out var list))
            {
                var handlers = list.ToArray();
                foreach(var del in handlers)
                {
                    if (del is Action<T> action)
                        action?.Invoke(evt);
                }
            }
        }

        public void ClearAll() { _subscribers.Clear(); }
    }
}