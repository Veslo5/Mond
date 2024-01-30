﻿using System;

namespace Mond.Binding
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MondModuleAttribute : MondBindClassAttribute
    {
        public MondModuleAttribute(string name = null)
            : base(name)
        {
        }
    }
}
