﻿/* ****************************************************************************
 * Copyright (c) 2015 Greg Fullman 
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution.
 * By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudioTools;
using VSGenero.Analysis.Parsing;
using VSGenero.Analysis;
using Shell = Microsoft.VisualStudio.Shell;

namespace VSGenero.EditorExtensions.Intellisense
{
    /// <summary>
    /// This enum allows for errors to be updated in different levels for a task provider key.
    /// </summary>
    public enum TaskLevel
    {
        All,
        Syntax,
        Semantics,
        Comment,
        /// <summary>
        /// Use the Build enum to specify errors that occurred specifically during a build.
        /// </summary>
        Build
    }

    public enum ErrorType
    {
        None,
        CompilerError,
        OtherError,
        SyntaxError,
        Warning
    }

    class TaskProviderItem
    {
        private readonly string _message;
        private readonly ITrackingSpan _span;
        private readonly SourceSpan _rawSpan;
        private readonly VSTASKPRIORITY _priority;
        private readonly VSTASKCATEGORY _category;
        private readonly bool _squiggle;
        private readonly ITextSnapshot _snapshot;
        private readonly IServiceProvider _serviceProvider;
        private readonly TaskLevel _level;
        private readonly ErrorType _errorType;

        public TaskLevel Level
        {
            get { return _level; }
        }

        internal TaskProviderItem(
            IServiceProvider serviceProvider,
            string message,
            SourceSpan rawSpan,
            VSTASKPRIORITY priority,
            VSTASKCATEGORY category,
            bool squiggle,
            ITextSnapshot snapshot,
            TaskLevel level,
            ErrorType errorType
        )
        {
            _serviceProvider = serviceProvider;
            _message = message;
            _rawSpan = rawSpan;
            _snapshot = snapshot;
            _span = snapshot != null ? CreateSpan(snapshot, rawSpan) : null;
            _rawSpan = rawSpan;
            _priority = priority;
            _category = category;
            _squiggle = squiggle;
            _level = level;
            _errorType = errorType;
        }

        private string ErrorType
        {
            get
            {
                switch (_errorType)
                {
                    case Intellisense.ErrorType.SyntaxError:
                        return PredefinedErrorTypeNames.SyntaxError;
                    case Intellisense.ErrorType.OtherError:
                        return PredefinedErrorTypeNames.OtherError;
                    case Intellisense.ErrorType.CompilerError:
                        return PredefinedErrorTypeNames.CompilerError;
                    case Intellisense.ErrorType.Warning:
                        return PredefinedErrorTypeNames.Warning;
                    default:
                        return string.Empty;
                }
            }
        }

        #region Conversion Functions

        public bool IsValid
        {
            get
            {
                if (!_squiggle || _snapshot == null || _span == null || string.IsNullOrEmpty(ErrorType))
                {
                    return false;
                }
                return true;
            }
        }

        public void CreateSquiggleSpan(SimpleTagger<ErrorTag> tagger)
        {
            var result = tagger.CreateTagSpan(_span, new ErrorTag(ErrorType, _message));
        }

        public ITextSnapshot Snapshot
        {
            get
            {
                return _snapshot;
            }
        }

        public ErrorTaskItem ToErrorTaskItem(EntryKey key)
        {
            return new ErrorTaskItem(
                _serviceProvider,
                _rawSpan,
                _message,
                key.Filepath ?? string.Empty
            )
            {
                Priority = _priority,
                Category = _category
            };
        }

        #endregion

        private static ITrackingSpan CreateSpan(ITextSnapshot snapshot, SourceSpan span)
        {
            Debug.Assert(span.Start.Index >= 0);
            var res = new Span(
                span.Start.Index,
                Math.Min(span.End.Index - span.Start.Index, Math.Max(snapshot.Length - span.Start.Index, 0))
            );
            Debug.Assert(res.End <= snapshot.Length);
            return snapshot.CreateTrackingSpan(res, SpanTrackingMode.EdgeNegative);
        }
    }

    sealed class TaskProviderItemFactory
    {
        private readonly ITextSnapshot _snapshot;

        public TaskProviderItemFactory(
            ITextSnapshot snapshot
        )
        {
            _snapshot = snapshot;
        }

        #region Factory Functions

        public TaskProviderItem FromErrorResult(IServiceProvider serviceProvider, ErrorResult result, VSTASKPRIORITY priority, VSTASKCATEGORY category, TaskLevel level, ErrorType errorType)
        {
            return new TaskProviderItem(
                serviceProvider,
                result.Message,
                result.Span,
                priority,
                category,
                true,
                _snapshot,
                level,
                errorType
            );
        }

        #endregion
    }

    struct EntryKey : IEquatable<EntryKey>
    {
        public string Filepath;
        public string Moniker;

        public static readonly EntryKey Empty = new EntryKey(null, null);

        public EntryKey(string filepath, string moniker)
        {
            Filepath = filepath;
            Moniker = moniker;
        }

        public override bool Equals(object obj)
        {
            return obj is EntryKey && Equals((EntryKey)obj);
        }

        public bool Equals(EntryKey other)
        {
            return Filepath.Equals(other.Filepath, StringComparison.OrdinalIgnoreCase) && Moniker == other.Moniker;
        }

        public override int GetHashCode()
        {
            return (Filepath == null ? 0 : Filepath.ToLower().GetHashCode()) ^ (Moniker ?? string.Empty).GetHashCode();
        }
    }

    abstract class WorkerMessage
    {
        private readonly EntryKey _key;
        private readonly List<TaskProviderItem> _items;

        protected WorkerMessage()
        {
            _key = EntryKey.Empty;
        }

        protected WorkerMessage(EntryKey key, List<TaskProviderItem> items)
        {
            _key = key;
            _items = items;
        }

        public abstract bool Apply(Dictionary<EntryKey, List<TaskProviderItem>> items, object itemsLock);

        // Factory methods
        public static WorkerMessage Clear(TaskLevel taskLevel = TaskLevel.All)
        {
            return new ClearMessage(EntryKey.Empty, taskLevel);
        }

        public static WorkerMessage Clear(string filepath, string moniker, TaskLevel taskLevel = TaskLevel.All)
        {
            return new ClearMessage(new EntryKey(filepath, moniker), taskLevel);
        }

        public static WorkerMessage Replace(string filepath, string moniker, List<TaskProviderItem> items, TaskLevel level)
        {
            return new ReplaceMessage(new EntryKey(filepath, moniker), items, level);
        }

        public static WorkerMessage Append(string filepath, string moniker, List<TaskProviderItem> items)
        {
            return new AppendMessage(new EntryKey(filepath, moniker), items);
        }

        public static WorkerMessage Flush(TaskCompletionSource<TimeSpan> taskSource)
        {
            return new FlushMessage(taskSource, DateTime.Now);
        }

        // Message implementations
        sealed class ReplaceMessage : WorkerMessage
        {
            private readonly TaskLevel _level;

            public ReplaceMessage(EntryKey key, List<TaskProviderItem> items, TaskLevel level)
                : base(key, items)
            {
                _level = level;
            }

            public override bool Apply(Dictionary<EntryKey, List<TaskProviderItem>> items, object itemsLock)
            {
                lock (itemsLock)
                {
                    if (!items.ContainsKey(_key))
                    {
                        items[_key] = _items;
                    }
                    else
                    {
                        items[_key].RemoveAll(x => x.Level.HasFlag(_level));
                        items[_key].AddRange(_items);
                    }
                    return true;
                }
            }
        }

        sealed class AppendMessage : WorkerMessage
        {
            public AppendMessage(EntryKey key, List<TaskProviderItem> items)
                : base(key, items)
            { }

            public override bool Apply(Dictionary<EntryKey, List<TaskProviderItem>> items, object itemsLock)
            {
                lock (itemsLock)
                {
                    List<TaskProviderItem> itemList;
                    if (items.TryGetValue(_key, out itemList))
                    {
                        itemList.AddRange(_items);
                    }
                    else
                    {
                        items[_key] = _items;
                    }
                    return true;
                }
            }
        }

        sealed class ClearMessage : WorkerMessage
        {
            private readonly TaskLevel _taskLevel;

            public ClearMessage(EntryKey key, TaskLevel taskLevel)
                : base(key, null)
            {
                _taskLevel = taskLevel;
            }

            public override bool Apply(Dictionary<EntryKey, List<TaskProviderItem>> items, object itemsLock)
            {
                lock (itemsLock)
                {
                    if (_key.Filepath != null && items.ContainsKey(_key))
                    {
                        if (_taskLevel == TaskLevel.All)
                        {
                            items.Remove(_key);
                        }
                        else
                        {
                            items[_key].RemoveAll(x => x.Level.HasFlag(_taskLevel));
                            if (items[_key].Count == 0)
                                items.Remove(_key);
                        }
                    }
                    else
                    {
                        if (_taskLevel == TaskLevel.All)
                        {
                            items.Clear();
                        }
                        else
                        {
                            List<EntryKey> remove = new List<EntryKey>();
                            foreach (var item in items)
                            {
                                item.Value.RemoveAll(x => x.Level.HasFlag(_taskLevel));
                                if (item.Value.Count == 0)
                                    remove.Add(item.Key);
                            }
                            foreach (var rem in remove)
                                items.Remove(rem);
                        }
                    }
                    // Always return true to ensure the refresh occurs
                    return true;
                }
            }
        }

        internal sealed class FlushMessage : WorkerMessage
        {
            private readonly TaskCompletionSource<TimeSpan> _tcs;
            private readonly DateTime _start;

            public FlushMessage(TaskCompletionSource<TimeSpan> taskSource, DateTime start)
                : base(EntryKey.Empty, null)
            {
                _tcs = taskSource;
                _start = start;
            }

            public override bool Apply(Dictionary<EntryKey, List<TaskProviderItem>> items, object itemsLock)
            {
                _tcs.SetResult(DateTime.Now - _start);
                return false;
            }
        }
    }

    abstract class TaskProvider : IVsTaskProvider, IDisposable
    {
        private readonly Dictionary<EntryKey, List<TaskProviderItem>> _items;
        private readonly Dictionary<EntryKey, HashSet<ITextBuffer>> _errorSources;
        private readonly object _itemsLock = new object();
        private uint _cookie;
        private readonly IVsTaskList _taskList;
        internal readonly IErrorProviderFactory _errorProvider;
        protected readonly IServiceProvider _serviceProvider;

        private bool _hasWorker;
        private readonly BlockingCollection<WorkerMessage> _workerQueue;

        protected virtual bool UpdatesSquiggles { get { return true; } }

        public TaskProvider(IServiceProvider serviceProvider, IVsTaskList taskList, IErrorProviderFactory errorProvider)
        {
            _serviceProvider = serviceProvider;
            _items = new Dictionary<EntryKey, List<TaskProviderItem>>();
            _errorSources = new Dictionary<EntryKey, HashSet<ITextBuffer>>();

            _taskList = taskList;
            _errorProvider = errorProvider;
            _workerQueue = new BlockingCollection<WorkerMessage>();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_workerQueue)
                {
                    if (_hasWorker)
                    {
                        _hasWorker = false;
                        _workerQueue.CompleteAdding();
                    }
                    else
                    {
                        _workerQueue.Dispose();
                    }
                }
                lock (_itemsLock)
                {
                    _items.Clear();
                }
                RefreshAsync().DoNotWait();
                if (_taskList != null)
                {
                    _taskList.UnregisterTaskProvider(_cookie);
                }
            }
        }

        public uint Cookie
        {
            get
            {
                return _cookie;
            }
        }

        /// <summary>
        /// Replaces the items for the specified entry.
        /// </summary>
        public void ReplaceItems(string filepath, string moniker, List<TaskProviderItem> items, TaskLevel level)
        {
            if (!HasErrorSource(filepath, moniker))
            {
                AddErrorSource(filepath, moniker);
            }

            SendMessage(WorkerMessage.Replace(filepath, moniker, items, level));
        }

        public List<TaskProviderItem> GetItems(string filepath, string moniker)
        {
            lock (_itemsLock)
            {
                EntryKey ek = new EntryKey(filepath, moniker);
                List<TaskProviderItem> items;
                _items.TryGetValue(ek, out items);
                return items;
            }
        }

        /// <summary>
        /// Adds items to the specified entry's existing items.
        /// </summary>
        public void AddItems(string filepath, string moniker, List<TaskProviderItem> items)
        {
            SendMessage(WorkerMessage.Append(filepath, moniker, items));
        }

        /// <summary>
        /// Removes all items from all entries.
        /// </summary>
        public void ClearAll(TaskLevel taskLevel = TaskLevel.All)
        {
            SendMessage(WorkerMessage.Clear(taskLevel));
        }

        /// <summary>
        /// Removes all items for the specified entry.
        /// </summary>
        public void Clear(string filePath, string moniker, TaskLevel taskLevel = TaskLevel.All)
        {
            List<EntryKey> remKeys = null;
            lock (_errorSources)
            {
                remKeys = _errorSources.Where(x => x.Key.Filepath.StartsWith(filePath, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList();
            }
            for (int i = 0; i < remKeys.Count; i++)
            {
                SendMessage(WorkerMessage.Clear(remKeys[i].Filepath, moniker, taskLevel));
            }
        }

        /// <summary>
        /// Waits for all messages to clear the queue. This typically takes at
        /// least one second, since that is the timeout on the worker thread.
        /// </summary>
        /// <returns>
        /// The time between when flush was called and the queue completed.
        /// </returns>
        public Task<TimeSpan> FlushAsync()
        {
            var tcs = new TaskCompletionSource<TimeSpan>();
            SendMessage(WorkerMessage.Flush(tcs));
            return tcs.Task;
        }

        public bool HasErrorSource(string filepath, string moniker)
        {
            lock (_errorSources)
            {
                return _errorSources.ContainsKey(new EntryKey(filepath, moniker));
            }
        }

        /// <summary>
        /// Adds the buffer to be tracked for reporting squiggles and error list entries
        /// for the given project entry and moniker for the error source.
        /// </summary>
        public void AddBufferForErrorSource(string filepath, string moniker, ITextBuffer buffer)
        {
            lock (_errorSources)
            {
                var key = new EntryKey(filepath, moniker);
                HashSet<ITextBuffer> buffers;
                if (!_errorSources.TryGetValue(key, out buffers))
                {
                    _errorSources[new EntryKey(filepath, moniker)] = buffers = new HashSet<ITextBuffer>();
                }
                if (buffer != null)
                    buffers.Add(buffer);
            }
        }

        public void AddErrorSource(string filepath, string moniker)
        {
            var key = new EntryKey(filepath, moniker);
            lock (_errorSources)
            {
                if (!_errorSources.ContainsKey(key))
                {
                    _errorSources[new EntryKey(filepath, moniker)] = new HashSet<ITextBuffer>();
                }
            }
        }

        /// <summary>
        /// Removes the buffer from tracking for reporting squiggles and error list entries
        /// for the given project entry and moniker for the error source.
        /// </summary>
        public void RemoveBufferForErrorSource(string filepath, string moniker, ITextBuffer buffer)
        {
            lock (_errorSources)
            {
                var key = new EntryKey(filepath, moniker);
                HashSet<ITextBuffer> buffers;
                if (_errorSources.TryGetValue(key, out buffers))
                {
                    buffers.Remove(buffer);
                }
            }
        }

        /// <summary>
        /// Clears all tracked buffers for the given project entry and moniker for
        /// the error source.
        /// </summary>
        public void ClearErrorSource(string filepath, string moniker)
        {
            lock (_errorSources)
            {
                _errorSources.Remove(new EntryKey(filepath, moniker));
            }
        }

        public void ClearErrorSource(string filePath)
        {
            lock (_errorSources)
            {
                var remKeys = _errorSources.Where(x => x.Key.Filepath.StartsWith(filePath, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList();
                foreach (var key in remKeys)
                {
                    _errorSources.Remove(key);
                }
            }
        }

        #region Internal Worker Thread

        private void Worker()
        {
            var flushMessages = new Queue<WorkerMessage>();
            var cts = new CancellationTokenSource();
            bool changed = false;
            var lastUpdateTime = DateTime.Now;

            try
            {
                // First time through, we don't want to abort the queue. There
                // should be at least one message or the worker would not have
                // been started.

                foreach (var msg in _workerQueue.GetConsumingEnumerable(cts.Token))
                {
                    // Prevent timeouts while processing the message
                    cts.CancelAfter(-1);

                    if (msg is WorkerMessage.FlushMessage)
                    {
                        // Keep flush messages until we've exited the loop
                        flushMessages.Enqueue(msg);
                    }
                    else
                    {
                        // Apply the message to our collection
                        changed |= msg.Apply(_items, _itemsLock);
                    }

                    // Every second, we want to force another update
                    if (changed)
                    {
                        var currentTime = DateTime.Now;
                        if ((currentTime - lastUpdateTime).TotalMilliseconds > 1000)
                        {
                            Refresh();
                            lastUpdateTime = currentTime;
                            changed = false;
                        }
                    }

                    // Reset the timeout back to 1 second
                    cts.CancelAfter(1000);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the timeout expires
            }
            catch (ObjectDisposedException ex)
            {
                // We have been disposed.
                Debug.Assert(
                    ex.ObjectName == "BlockingCollection",
                    "Handled ObjectDisposedException for the wrong type"
                );
                return;
            }
            finally
            {
                lock (_workerQueue)
                {
                    _hasWorker = false;
                }
            }

            // Handle any changes that weren't handled in the loop
            if (changed)
            {
                Refresh();
            }

            // Notify all the flush messages we received
            while (flushMessages.Any())
            {
                var msg = flushMessages.Dequeue();
                msg.Apply(_items, _itemsLock);
            }

            try
            {
                if (_workerQueue.IsCompleted)
                {
                    _workerQueue.Dispose();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool _isRefreshing = false;

        private void Refresh()
        {
            try
            {
                if (_taskList != null || _errorProvider != null && !_isRefreshing)
                {
                    _serviceProvider.GetUIThread().MustNotBeCalledFromUIThread();
                    RefreshAsync().WaitAndHandleAllExceptions("VSGenero", GetType());
                }
            }
            catch(Exception ex)
            {

            }
        }

        private async Task RefreshAsync()
        {
            _isRefreshing = true;
            var buffers = new HashSet<ITextBuffer>();
            var bufferToErrorList = new Dictionary<ITextBuffer, List<TaskProviderItem>>();

            if (_errorProvider != null)
            {
                lock (_errorSources)
                {
                    foreach (var kv in _errorSources)
                    {
                        List<TaskProviderItem> items;
                        buffers.UnionWith(kv.Value);

                        lock (_itemsLock)
                        {
                            if (!_items.TryGetValue(kv.Key, out items))
                            {
                                continue;
                            }

                            foreach (var item in items)
                            {
                                if (item.IsValid)
                                {
                                    List<TaskProviderItem> itemList;
                                    if (!bufferToErrorList.TryGetValue(item.Snapshot.TextBuffer, out itemList))
                                    {
                                        bufferToErrorList[item.Snapshot.TextBuffer] = itemList = new List<TaskProviderItem>();
                                    }

                                    itemList.Add(item);
                                }
                            }
                        }
                    }
                }
            }

            try
            {
                await _serviceProvider.GetUIThread().InvokeAsync(() =>
                {
                    if (_taskList != null)
                    {
                        if (_cookie == 0)
                        {
                            ErrorHandler.ThrowOnFailure(_taskList.RegisterTaskProvider(this, out _cookie));
                        }
                        try
                        {
                            _taskList.RefreshTasks(_cookie);
                        }
                        catch (InvalidComObjectException)
                        {
                            // DevDiv2 759317 - Watson bug, COM object can go away...
                        }
                    }

                    if (UpdatesSquiggles && _errorProvider != null)
                    {
                        foreach (var kv in bufferToErrorList)
                        {
                            var tagger = _errorProvider.GetErrorTagger(kv.Key);
                            if (tagger == null)
                            {
                                continue;
                            }

                            if (buffers.Remove(kv.Key))
                            {
                                tagger.RemoveTagSpans(span => span.Span.TextBuffer == kv.Key);
                            }

                            foreach (var taskProviderItem in kv.Value)
                            {
                                taskProviderItem.CreateSquiggleSpan(tagger);
                            }
                        }

                        if (buffers.Any())
                        {
                            // Clear tags for any remaining buffers.
                            foreach (var buffer in buffers)
                            {
                                var tagger = _errorProvider.GetErrorTagger(buffer);
                                tagger.RemoveTagSpans(span => span.Span.TextBuffer == buffer);
                            }
                        }
                    }
                });
            }
            catch (Exception)
            { }
            _isRefreshing = false;
        }

        private void SendMessage(WorkerMessage message)
        {
            lock (_workerQueue)
            {
                try
                {
                    _workerQueue.Add(message);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                if (!_hasWorker)
                {
                    _hasWorker = true;
                    Task.Run(() => Worker())
                        .HandleAllExceptions(/*SR.ProductName*/"VSGenero", GetType())
                        .DoNotWait();
                }
            }
        }

        #endregion

        #region IVsTaskProvider Members

        public int EnumTaskItems(out IVsEnumTaskItems ppenum)
        {
            lock (_itemsLock)
            {
                ppenum = new TaskEnum(_items
                    .Where(x => x.Key.Filepath != null)   // don't report REPL window errors in the error list, you can't naviagate to them
                    .SelectMany(kv => kv.Value.Select(i => i.ToErrorTaskItem(kv.Key)))
                    .ToArray()
                );
            }
            return VSConstants.S_OK;
        }

        public int ImageList(out IntPtr phImageList)
        {
            // not necessary if we report our category as build compile.
            phImageList = IntPtr.Zero;
            return VSConstants.E_NOTIMPL;
        }

        public int OnTaskListFinalRelease(IVsTaskList pTaskList)
        {
            return VSConstants.S_OK;
        }

        public int ReRegistrationKey(out string pbstrKey)
        {
            pbstrKey = null;
            return VSConstants.E_NOTIMPL;
        }

        public int SubcategoryList(uint cbstr, string[] rgbstr, out uint pcActual)
        {
            pcActual = 0;
            return VSConstants.S_OK;
        }

        #endregion
    }

    class TaskEnum : IVsEnumTaskItems
    {
        private readonly IEnumerable<ErrorTaskItem> _enumerable;
        private IEnumerator<ErrorTaskItem> _enumerator;

        public TaskEnum(IEnumerable<ErrorTaskItem> items)
        {
            _enumerable = items;
            _enumerator = _enumerable.GetEnumerator();
        }

        public int Clone(out IVsEnumTaskItems ppenum)
        {
            ppenum = new TaskEnum(_enumerable);
            return VSConstants.S_OK;
        }

        public int Next(uint celt, IVsTaskItem[] rgelt, uint[] pceltFetched = null)
        {
            bool fetchedAny = false;

            if (pceltFetched != null && pceltFetched.Length > 0)
            {
                pceltFetched[0] = 0;
            }

            for (int i = 0; i < celt && _enumerator.MoveNext(); i++)
            {
                if (pceltFetched != null && pceltFetched.Length > 0)
                {
                    pceltFetched[0] = (uint)i + 1;
                }
                rgelt[i] = _enumerator.Current;
                fetchedAny = true;
            }

            return fetchedAny ? VSConstants.S_OK : VSConstants.S_FALSE;
        }

        public int Reset()
        {
            _enumerator = _enumerable.GetEnumerator();
            return VSConstants.S_OK;
        }

        public int Skip(uint celt)
        {
            while (celt != 0 && _enumerator.MoveNext())
            {
                celt--;
            }
            return VSConstants.S_OK;
        }
    }

    class ErrorTaskItem : IVsTaskItem
    {
        private readonly IServiceProvider _serviceProvider;

        public ErrorTaskItem(
            IServiceProvider serviceProvider,
            SourceSpan span,
            string message,
            string sourceFile
        )
        {
            _serviceProvider = serviceProvider;
            Span = span;
            Message = message;
            SourceFile = sourceFile;
            Category = VSTASKCATEGORY.CAT_BUILDCOMPILE;
            Priority = VSTASKPRIORITY.TP_NORMAL;

            MessageIsReadOnly = true;
            IsCheckedIsReadOnly = true;
            PriorityIsReadOnly = true;
        }

        public SourceSpan Span { get; private set; }
        public string Message { get; set; }
        public string SourceFile { get; set; }
        public VSTASKCATEGORY Category { get; set; }
        public VSTASKPRIORITY Priority { get; set; }
        public bool CanDelete { get; set; }
        public bool IsChecked { get; set; }

        public bool MessageIsReadOnly { get; set; }
        public bool IsCheckedIsReadOnly { get; set; }
        public bool PriorityIsReadOnly { get; set; }

        int IVsTaskItem.CanDelete(out int pfCanDelete)
        {
            pfCanDelete = CanDelete ? 1 : 0;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.Category(VSTASKCATEGORY[] pCat)
        {
            pCat[0] = Category;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.Column(out int piCol)
        {
            if (Span.Start.Line == 1 && Span.Start.Column == 1 && Span.Start.Index != 0)
            {
                // we don't have the column number calculated
                piCol = 0;
                return VSConstants.E_FAIL;
            }
            piCol = Span.Start.Column - 1;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.Document(out string pbstrMkDocument)
        {
            pbstrMkDocument = SourceFile;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.HasHelp(out int pfHasHelp)
        {
            pfHasHelp = 0;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.ImageListIndex(out int pIndex)
        {
            pIndex = 0;
            return VSConstants.E_NOTIMPL;
        }

        int IVsTaskItem.IsReadOnly(VSTASKFIELD field, out int pfReadOnly)
        {
            switch (field)
            {
                case VSTASKFIELD.FLD_CHECKED:
                    pfReadOnly = IsCheckedIsReadOnly ? 1 : 0;
                    break;
                case VSTASKFIELD.FLD_DESCRIPTION:
                    pfReadOnly = MessageIsReadOnly ? 1 : 0;
                    break;
                case VSTASKFIELD.FLD_PRIORITY:
                    pfReadOnly = PriorityIsReadOnly ? 1 : 0;
                    break;
                case VSTASKFIELD.FLD_BITMAP:
                case VSTASKFIELD.FLD_CATEGORY:
                case VSTASKFIELD.FLD_COLUMN:
                case VSTASKFIELD.FLD_CUSTOM:
                case VSTASKFIELD.FLD_FILE:
                case VSTASKFIELD.FLD_LINE:
                case VSTASKFIELD.FLD_PROVIDERKNOWSORDER:
                case VSTASKFIELD.FLD_SUBCATEGORY:
                default:
                    pfReadOnly = 1;
                    break;
            }
            return VSConstants.S_OK;
        }

        int IVsTaskItem.Line(out int piLine)
        {
            if (Span.Start.Line == 1 && Span.Start.Column == 1 && Span.Start.Index != 0)
            {
                // we don't have the line number calculated
                piLine = 0;
                return VSConstants.E_FAIL;
            }
            piLine = Span.Start.Line - 1;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.NavigateTo()
        {
            try
            {
                if (Span.Start.Line == 1 && Span.Start.Column == 1 && Span.Start.Index != 0)
                {
                    // we have just an absolute index, use that to naviagte
                    VSGeneroPackage.NavigateTo(SourceFile, Guid.Empty, Span.Start.Index);
                }
                else
                {
                    VSGeneroPackage.NavigateTo(SourceFile, Guid.Empty, Span.Start.Line - 1, Span.Start.Column - 1);
                }
                return VSConstants.S_OK;
            }
            catch (DirectoryNotFoundException)
            {
                // This may happen when the error was in a file that's located inside a .zip archive.
                // Let's walk the path and see if it is indeed the case.
                for (var path = SourceFile; CommonUtils.IsValidPath(path); path = Path.GetDirectoryName(path))
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    var ext = Path.GetExtension(path);
                    if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".egg", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "Opening source files contained in .zip archives is not supported",
                            "Cannot open file",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                        return VSConstants.S_FALSE;
                    }
                }
                // If it failed for some other reason, let caller handle it.
                throw;
            }
        }

        int IVsTaskItem.NavigateToHelp()
        {
            return VSConstants.E_NOTIMPL;
        }

        int IVsTaskItem.OnDeleteTask()
        {
            return VSConstants.E_NOTIMPL;
        }

        int IVsTaskItem.OnFilterTask(int fVisible)
        {
            return VSConstants.E_NOTIMPL;
        }

        int IVsTaskItem.SubcategoryIndex(out int pIndex)
        {
            pIndex = 0;
            return VSConstants.E_NOTIMPL;
        }

        int IVsTaskItem.get_Checked(out int pfChecked)
        {
            pfChecked = IsChecked ? 1 : 0;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.get_Priority(VSTASKPRIORITY[] ptpPriority)
        {
            ptpPriority[0] = Priority;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.get_Text(out string pbstrName)
        {
            pbstrName = Message;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.put_Checked(int fChecked)
        {
            if (IsCheckedIsReadOnly)
            {
                return VSConstants.E_NOTIMPL;
            }
            IsChecked = (fChecked != 0);
            return VSConstants.S_OK;
        }

        int IVsTaskItem.put_Priority(VSTASKPRIORITY tpPriority)
        {
            if (PriorityIsReadOnly)
            {
                return VSConstants.E_NOTIMPL;
            }
            Priority = tpPriority;
            return VSConstants.S_OK;
        }

        int IVsTaskItem.put_Text(string bstrName)
        {
            if (MessageIsReadOnly)
            {
                return VSConstants.E_NOTIMPL;
            }
            Message = bstrName;
            return VSConstants.S_OK;
        }
    }

    // Two distinct types are used to distinguish the two in the VS service registry.

    sealed class ErrorTaskProvider : TaskProvider
    {
        private readonly IVsErrorList _errorList;

        public ErrorTaskProvider(IServiceProvider serviceProvider, IVsTaskList taskList, IErrorProviderFactory errorProvider)
            : base(serviceProvider, taskList, errorProvider)
        {
            if (taskList is IVsErrorList)
                _errorList = taskList as IVsErrorList;
        }

        public void BringToFront()
        {
            if (!Shell.VsShellUtilities.ShellIsShuttingDown)
            {
                _errorList.BringToFront();
            }
        }

        public void ForceShowErrors()
        {
            if (!Shell.VsShellUtilities.ShellIsShuttingDown)
            {
                _errorList.ForceShowErrors();
            }
        }
    }

    sealed class CommentTaskProvider : TaskProvider, IVsTaskListEvents
    {
        private volatile Dictionary<string, VSTASKPRIORITY> _tokens;

        protected override bool UpdatesSquiggles
        {
            get
            {
                return false;
            }
        }

        public CommentTaskProvider(IServiceProvider serviceProvider, IVsTaskList taskList, IErrorProviderFactory errorProvider)
            : base(serviceProvider, taskList, errorProvider)
        {
            RefreshTokens();
        }

        public Dictionary<string, VSTASKPRIORITY> Tokens
        {
            get { return _tokens; }
        }

        public event EventHandler TokensChanged;

        public int OnCommentTaskInfoChanged()
        {
            RefreshTokens();
            return VSConstants.S_OK;
        }

        // Retrieves token settings as defined by user in Tools -> Options -> Environment -> Task List.
        private void RefreshTokens()
        {
            var taskInfo = _serviceProvider.GetService(typeof(SVsTaskList)) as IVsCommentTaskInfo;
            if (taskInfo == null)
            {
                return;
            }

            IVsEnumCommentTaskTokens enumTokens;
            ErrorHandler.ThrowOnFailure(taskInfo.EnumTokens(out enumTokens));

            var newTokens = new Dictionary<string, VSTASKPRIORITY>();

            var token = new IVsCommentTaskToken[1];
            uint fetched;
            string text;
            var priority = new VSTASKPRIORITY[1];

            // DevDiv bug 1135485: EnumCommentTaskTokens.Next returns E_FAIL instead of S_FALSE
            while (enumTokens.Next(1, token, out fetched) == VSConstants.S_OK && fetched > 0)
            {
                ErrorHandler.ThrowOnFailure(token[0].Text(out text));
                ErrorHandler.ThrowOnFailure(token[0].Priority(priority));
                newTokens[text] = priority[0];
            }

            _tokens = newTokens;

            var tokensChanged = TokensChanged;
            if (tokensChanged != null)
            {
                tokensChanged(this, EventArgs.Empty);
            }
        }
    }
}
