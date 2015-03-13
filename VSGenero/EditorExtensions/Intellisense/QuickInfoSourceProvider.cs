﻿using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSGenero.EditorExtensions.Intellisense
{
    [Export(typeof(IQuickInfoSourceProvider)), ContentType(VSGeneroConstants.ContentType4GL), ContentType(VSGeneroConstants.ContentTypePER), Order, Name("Python Quick Info Source")]
    class QuickInfoSourceProvider : IQuickInfoSourceProvider
    {
        public IQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new QuickInfoSource(this, textBuffer);
        }
    }
}