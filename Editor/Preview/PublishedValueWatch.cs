#if DOCME_NDMF && DOCME_VRCSDK3A
using System;
using nadena.dev.ndmf.preview;

namespace DocMe.AvatarDoctor.Editor
{
    /// <summary>Keeps a handler attached to an NDMF PublishedValue, whose OnChange fires once per subscription.</summary>
    public sealed class PublishedValueWatch<T>
    {
        readonly PublishedValue<T> value;
        readonly Action<T> handler;
        bool active;

        public PublishedValueWatch(PublishedValue<T> value, Action<T> handler)
        {
            this.value = value;
            this.handler = handler;
        }

        public void Start()
        {
            if (active) return;
            active = true;
            value.OnChange += OnChange;
        }

        public void Stop()
        {
            active = false;
            value.OnChange -= OnChange;
        }

        void OnChange(T current)
        {
            if (!active) return;
            value.OnChange += OnChange;
            handler(current);
        }
    }
}
#endif
