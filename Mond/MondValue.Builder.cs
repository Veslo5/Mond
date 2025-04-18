﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using JetBrains.Annotations;

namespace Mond
{
    public partial struct MondValue
    {
        public static MondValue Number(double value)
        {
            return new MondValue(value);
        }

        public static MondValue String([NotNull] string value)
        {
            return new MondValue(value);
        }

        public static MondValue Function([NotNull] MondFunction value)
        {
            return new MondValue(value);
        }

        public static MondValue Object([NotNull] MondState state, IEnumerable<KeyValuePair<MondValue, MondValue>> entries = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var value = new MondValue(state);

            if (entries != null)
            {
                var dict = value.AsDictionary;
                foreach (var kvp in entries)
                {
                    dict.Add(kvp.Key, kvp.Value);
                }
            }

            return value;
        }

        public static MondValue Object(IEnumerable<KeyValuePair<MondValue, MondValue>> entries = null)
        {
            var value = new MondValue(MondValueType.Object);

            if (entries != null)
            {
                var dict = value.AsDictionary;
                foreach (var kvp in entries)
                {
                    dict.Add(kvp.Key, kvp.Value);
                }
            }

            return value;
        }

        public static MondValue Array(IEnumerable<MondValue> values = null)
        {
            if (values == null)
            {
                return new MondValue(MondValueType.Array);
            }

            return new MondValue(values);
        }

        public static MondValue ProxyObject(MondValue target, MondValue handler, [NotNull] MondState state)
        {
            if (handler.Type != MondValueType.Object)
                throw new ArgumentException("Proxy handler must be an object");

            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var value = new MondValue(target, handler, state);
            return value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static MondValue ClassInstance<T>([NotNull] MondState state, [NotNull] T instance, string prototypeName)
            where T : class
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            if (!state.TryFindPrototype(prototypeName, out var prototype))
                throw new MondRuntimeException($"Could not find prototype for bound class in the current Mond state: {prototypeName}");

            var obj = new MondValue(state);
            obj.Prototype = prototype;
            obj.UserData = instance;
            obj.Lock();
            return obj;
        }
    }
}
