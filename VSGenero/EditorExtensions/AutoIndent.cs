﻿using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using VSGenero.Analysis;
using VSGenero.Analysis.Parsing;
using VSGenero.Analysis.Parsing.AST;

namespace VSGenero.EditorExtensions
{
    internal static partial class AutoIndent
    {
        internal static int GetIndentation(string line, int tabSize)
        {
            int res = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == ' ')
                {
                    res++;
                }
                else if (line[i] == '\t')
                {
                    res += tabSize;
                }
                else
                {
                    break;
                }
            }
            return res;
        }

        private struct LineInfo
        {
            public static readonly LineInfo Empty = new LineInfo();
            public bool NeedsUpdate;
            public int Indentation;
            public bool ShouldIndentAfter;
            public bool ShouldDedentAfter;
        }

        private static int CalculateIndentation(string baseline, ITextSnapshotLine line, IEditorOptions options, IClassifier classifier, ITextView textView)
        {
            int indentation = GetIndentation(baseline, options.GetTabSize());
            int tabSize = options.GetIndentSize();
            var tokens = classifier.GetClassificationSpans(line.Extent);
            if (tokens.Count > 0 && !IsUnterminatedStringToken(tokens[tokens.Count - 1]))
            {
                int tokenIndex = tokens.Count - 1;

                while (tokenIndex >= 0 &&
                    (tokens[tokenIndex].ClassificationType.IsOfType(PredefinedClassificationTypeNames.Comment) ||
                    tokens[tokenIndex].ClassificationType.IsOfType(PredefinedClassificationTypeNames.WhiteSpace)))
                {
                    tokenIndex--;
                }

                if (tokenIndex < 0)
                {
                    return indentation;
                }

                if (Genero4glReverseParser.IsExplicitLineJoin(tokens[tokenIndex]))
                {
                    // explicit line continuation, we indent 1 level for the continued line unless
                    // we're already indented because of multiple line continuation characters.

                    indentation = GetIndentation(line.GetText(), options.GetTabSize());
                    var joinedLine = tokens[tokenIndex].Span.Start.GetContainingLine();
                    if (joinedLine.LineNumber > 0)
                    {
                        var prevLineSpans = classifier.GetClassificationSpans(tokens[tokenIndex].Span.Snapshot.GetLineFromLineNumber(joinedLine.LineNumber - 1).Extent);
                        if (prevLineSpans.Count == 0 || !Genero4glReverseParser.IsExplicitLineJoin(prevLineSpans[prevLineSpans.Count - 1]))
                        {
                            indentation += tabSize;
                        }
                    }
                    else
                    {
                        indentation += tabSize;
                    }

                    return indentation;
                }

                string sline = tokens[tokenIndex].Span.GetText();
                var lastChar = sline.Length == 0 ? '\0' : sline[sline.Length - 1];

                // use the expression parser to figure out if we're in a grouping...
                var spans = textView.BufferGraph.MapDownToFirstMatch(
                    tokens[tokenIndex].Span,
                    SpanTrackingMode.EdgePositive,
                    PythonContentTypePrediciate
                );
                if (spans.Count == 0)
                {
                    return indentation;
                }

                var revParser = new Genero4glReverseParser(
                        spans[0].Snapshot,
                        spans[0].Snapshot.TextBuffer,
                        spans[0].Snapshot.CreateTrackingSpan(
                            spans[0].Span,
                            SpanTrackingMode.EdgePositive
                        )
                    );

                var tokenStack = new List<ClassificationSpan>();// new System.Collections.Generic.Stack<ClassificationSpan>();
                //tokenStack.Push(null);  // end with an implicit newline
                tokenStack.Insert(0, null);
                bool endAtNextNull = false;

                foreach (var token in revParser)
                {
                    //tokenStack.Push(token);
                    tokenStack.Insert(0, token);
                    if (token == null && endAtNextNull)
                    {
                        break;
                    }
                    else if (token != null &&
                     token.ClassificationType == revParser.Classifier.Provider.Keyword)
                    {
                        var tok = Tokens.GetToken(token.Span.GetText());
                        if (tok != null && GeneroAst.ValidStatementKeywords.Contains(tok.Kind))
                        {
                            switch (tok.Kind)
                            {
                                // Handle any tokens that are valid statement keywords in the autocomplete context but not in the "statement start" context
                                case TokenKind.EndKeyword:
                                    continue;
                                default:
                                    endAtNextNull = true;
                                    break;
                            }
                        }
                    }
                }

                var indentStack = new System.Collections.Generic.Stack<LineInfo>();
                var current = LineInfo.Empty;
                List<CancelIndent> cancelIndent = null;
                int cancelIndentStartingAt = -1;
                TokenKind firstStatement = TokenKind.EndOfFile;
                TokenKind latestIndentChangeToken = TokenKind.EndOfFile;

                for (int i = 0; i < tokenStack.Count; i++)
                {
                    //while (tokenStack.Count > 0)
                    //{
                    var token = tokenStack[i];//tokenStack.Pop();
                    if (token == null)
                    {
                        current.NeedsUpdate = true;
                    }
                    else if (token.IsOpenGrouping())
                    {
                        indentStack.Push(current);
                        var start = token.Span.Start;
                        var line2 = start.GetContainingLine();
                        var next = tokenStack.Count > 0 ? tokenStack.Peek() : null;
                        if (next != null && next.Span.End <= line2.End)
                        {
                            current = new LineInfo
                            {
                                Indentation = start.Position - line2.Start.Position + 1
                            };
                        }
                        else
                        {
                            current = new LineInfo
                            {
                                Indentation = GetIndentation(line2.GetText(), tabSize) + tabSize
                            };
                        }
                    }
                    else if (token.IsCloseGrouping())
                    {
                        if (indentStack.Count > 0)
                        {
                            current = indentStack.Pop();
                        }
                        else
                        {
                            current.NeedsUpdate = true;
                        }
                    }
                    else if (Genero4glReverseParser.IsExplicitLineJoin(token))
                    {
                        while (token != null && i + 1 < tokenStack.Count /* tokenStack.Count > 0*/)
                        {
                            i++;
                            token = tokenStack[i];//.Pop();
                        }
                    }
                    else if (current.NeedsUpdate == true)
                    {
                        var tok = Tokens.GetToken(token.Span.GetText());
                        if (tok == null || !GeneroAst.ValidStatementKeywords.Contains(tok.Kind))
                        {
                            current.NeedsUpdate = false;
                        }
                        else
                        {
                            switch (tok.Kind)
                            {
                                // Handle any tokens that are valid statement keywords in the autocomplete context but not in the "statement start" context
                                case TokenKind.EndKeyword:
                                    current.NeedsUpdate = false;
                                    break;
                                default:
                                    {
                                        if (firstStatement == TokenKind.EndOfFile)
                                            firstStatement = tok.Kind;
                                        var line2 = token.Span.Start.GetContainingLine();
                                        current = new LineInfo
                                        {
                                            Indentation = GetIndentation(line2.GetText(), tabSize)
                                        };
                                        break;
                                    }
                            }
                        }
                    }

                    if (token != null && current.ShouldIndentAfter && cancelIndent != null)
                    {
                        // Check to see if we have following tokens that would cancel the current indent.
                        var tok = Tokens.GetToken(token.Span.GetText());
                        if (tok != null)
                        {
                            bool allPast = true;
                            bool cancel = false;
                            foreach (var ci in cancelIndent)
                            {
                                if (ci.TokensAhead < (i - cancelIndentStartingAt))
                                    continue;
                                else
                                {
                                    allPast = false;
                                    if (ci.TokensAhead == (i - cancelIndentStartingAt))
                                    {
                                        cancel = tok.Kind == ci.CancelToken;
                                        if (cancel)
                                            break;
                                    }
                                }
                            }
                            if (cancel)
                            {
                                current.ShouldIndentAfter = false;
                            }
                            if (cancel || allPast)
                            {
                                cancelIndent = null;
                                cancelIndentStartingAt = -1;
                                latestIndentChangeToken = TokenKind.EndOfFile;
                            }
                        }
                    }

                    if (token != null && ShouldDedentAfterKeyword(token))
                    {     // dedent after some statements
                        current.ShouldDedentAfter = true;
                    }

                    TokenKind tempChangeToken;
                    if (token != null && indentStack.Count == 0 && ShouldIndentAfterKeyword(token, out tempChangeToken, out cancelIndent))
                    {                               // except in a grouping
                        latestIndentChangeToken = tempChangeToken;
                        current.ShouldIndentAfter = true;
                        if (cancelIndent != null)
                            cancelIndentStartingAt = i;
                    }
                }

                if (tokenStack.Count > 2 &&
                    tokenStack[tokenStack.Count - 2] != null)
                {
                    if (latestIndentChangeToken != TokenKind.EndOfFile &&
                       _customIndentingRules.ContainsKey(latestIndentChangeToken))
                    {
                        var potentialIndent = _customIndentingRules[latestIndentChangeToken](tokenStack, tabSize);
                        if (potentialIndent != 0)
                        {
                            return potentialIndent;
                        }
                    }

                    // see if we have specific alignment rules
                    if (firstStatement != TokenKind.EndOfFile &&
                       _customIndentingRules.ContainsKey(firstStatement))
                    {
                        var potentialIndent = _customIndentingRules[firstStatement](tokenStack, tabSize);
                        if (potentialIndent != 0)
                        {
                            return potentialIndent;
                        }
                    }
                }

                indentation = current.Indentation +
                    (current.ShouldIndentAfter ? tabSize : 0) -
                    (current.ShouldDedentAfter ? tabSize : 0);
            }

            return indentation;
        }

        private static Dictionary<TokenKind, Func<List<ClassificationSpan>, int, int>> _customIndentingRules = new Dictionary<TokenKind, Func<List<ClassificationSpan>, int, int>>
        {
            { TokenKind.DefineKeyword, DefineStatementIndenting},
            { TokenKind.TypeKeyword, DefineStatementIndenting},
            { TokenKind.ConstantKeyword, DefineStatementIndenting},
            { TokenKind.RecordKeyword, RecordBlockIndenting},
            { TokenKind.CallKeyword, CallStatementIndenting}
        };

        private static int RecordBlockIndenting(List<ClassificationSpan> tokenList, int defaultTabSize)
        {
            if (tokenList[0] == null && tokenList[tokenList.Count - 1] == null)
            {
                if (tokenList.Count > 3)
                {
                    var lastTokText = tokenList[tokenList.Count - 2].Span.GetText();
                    if (lastTokText == ",")
                    {
                        var line = tokenList[tokenList.Count - 2].Span.Start.GetContainingLine();
                        return GetIndentation(line.GetText(), defaultTabSize);
                    }
                }
            }
            return 0;
        }

        private static int CallStatementIndenting(List<ClassificationSpan> tokenList, int defaultTabSize)
        {
            if (tokenList[0] == null && tokenList[tokenList.Count - 1] == null)
            {
                if (tokenList.Count > 3)
                {
                    var lastTokText = tokenList[tokenList.Count - 2].Span.GetText();
                    if (lastTokText == ",")
                    {
                        int lastIndex = tokenList.Count - 3;
                        while (tokenList[lastIndex] == null)
                            lastIndex--;
                        var line = tokenList[tokenList.Count - 2].Span.Start.GetContainingLine();
                        return tokenList[lastIndex].Span.Start - line.Start;
                    }
                }
            }
            return 0;
        }

        private static int DefineStatementIndenting(List<ClassificationSpan> tokenList, int defaultTabSize)
        {
            if (tokenList[0] == null && tokenList[tokenList.Count - 1] == null)
            {
                if (tokenList.Count > 3)
                {
                    var startTokText = tokenList[1].Span.GetText();
                    var startTok = Tokens.GetToken(startTokText);
                    if (startTok != null)
                    {
                        var lastTokText = tokenList[tokenList.Count - 2].Span.GetText();
                        var lastTok = Tokens.GetToken(lastTokText);
                        if (lastTok == null)
                            lastTok = Tokens.GetSymbolToken(lastTokText);
                        if(lastTok != null)
                        {
                            switch (lastTok.Kind)
                            {
                                case TokenKind.Comma:
                                    {
                                        switch (startTok.Kind)
                                        {
                                            case TokenKind.DefineKeyword:
                                            case TokenKind.ConstantKeyword:
                                            case TokenKind.TypeKeyword:
                                                {
                                                    int nextIndex = 2;
                                                    while (tokenList[nextIndex] == null)
                                                        nextIndex++;
                                                    // grab the next token and get its "indentation"
                                                    var line = tokenList[nextIndex].Span.Start.GetContainingLine();
                                                    return tokenList[nextIndex].Span.Start - line.Start;
                                                }
                                        }
                                        break;
                                    }
                                case TokenKind.RecordKeyword:
                                    {
                                        int lastIndex = tokenList.Count - 3;
                                        while (tokenList[lastIndex] == null)
                                            lastIndex--;
                                        var checkPrevTok = Tokens.GetToken(tokenList[lastIndex].Span.GetText());
                                        if (checkPrevTok == null || checkPrevTok.Kind != TokenKind.EndKeyword)
                                        {
                                            switch (startTok.Kind)
                                            {
                                                case TokenKind.DefineKeyword:
                                                case TokenKind.TypeKeyword:
                                                    {
                                                        // get the line of the last token
                                                        var line = tokenList[tokenList.Count - 2].Span.Start.GetContainingLine();
                                                        return GetIndentation(line.GetText(), defaultTabSize) + defaultTabSize;
                                                    }
                                            }
                                        }
                                        break;
                                    }
                            }
                        }
                    }
                }
            }
            return 0;
        }

        private static bool IsUnterminatedStringToken(ClassificationSpan lastToken)
        {
            if (lastToken.ClassificationType.IsOfType(PredefinedClassificationTypeNames.String))
            {
                var text = lastToken.Span.GetText();
                if (text.EndsWith("\"") || text.EndsWith("'"))
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        public static HashSet<TokenKind> BlockKeywordsContainsCheck = new HashSet<TokenKind>
        {
            TokenKind.RecordKeyword
        };

        public static Dictionary<TokenKind, List<CancelIndent>> BlockKeywords = new Dictionary<TokenKind, List<CancelIndent>>
        {
             { TokenKind.GlobalsKeyword, null },
             { TokenKind.RecordKeyword, new List<CancelIndent>
             {
                new CancelIndent { CancelToken = TokenKind.LikeKeyword, TokensAhead = 1 }
             }},
             { TokenKind.MainKeyword, null },
             { TokenKind.TryKeyword, null },
             { TokenKind.SqlKeyword, null },
             { TokenKind.FunctionKeyword, null },
             { TokenKind.IfKeyword, null },
             { TokenKind.ElseKeyword, null },
             { TokenKind.WhileKeyword,  null },
             { TokenKind.ForKeyword, null },
             { TokenKind.ForeachKeyword, null },
             { TokenKind.CaseKeyword, null },
        };

        public class CancelIndent
        {
            public TokenKind CancelToken { get; set; }
            public int TokensAhead { get; set; }
        }

        // TODO: eventually this will need to support mixed use (e.g. dialog, display, etc. blocks)
        public static Dictionary<TokenKind, TokenKind> SubBlockKeywords = new Dictionary<TokenKind, TokenKind>
        {
            { TokenKind.ElseKeyword, TokenKind.IfKeyword },
            { TokenKind.CatchKeyword, TokenKind.TryKeyword },
            { TokenKind.WhenKeyword, TokenKind.CaseKeyword },
            { TokenKind.OtherwiseKeyword, TokenKind.CaseKeyword }
        };

        private static bool ShouldIndentAfterKeyword(ClassificationSpan span, out TokenKind matchedToken, out List<CancelIndent> cancelIndent)
        {
            matchedToken = TokenKind.EndOfFile;
            cancelIndent = null;
            var tok = Tokens.GetToken(span.Span.GetText());
            if (tok != null)
            {
                if (BlockKeywords.TryGetValue(tok.Kind, out cancelIndent))
                {
                    matchedToken = tok.Kind;
                    return true;
                }
            }
            return false;
        }

        private static bool ShouldDedentAfterKeyword(ClassificationSpan span)
        {
            return span.ClassificationType.Classification == PredefinedClassificationTypeNames.Keyword && ShouldDedentAfterKeyword(span.Span.GetText());
        }

        private static bool ShouldDedentAfterKeyword(string keyword)
        {
            var tok = Tokens.GetToken(keyword);
            if (tok != null)
            {
                switch (tok.Kind)
                {
                    case TokenKind.EndKeyword:
                        return true;
                }
            }
            return false;
        }

        private static bool IsBlankLine(string lineText)
        {
            foreach (char c in lineText)
            {
                if (!Char.IsWhiteSpace(c))
                {
                    return false;
                }
            }
            return true;
        }

        private static void SkipPreceedingBlankLines(ITextSnapshotLine line, out string baselineText, out ITextSnapshotLine baseline)
        {
            string text;
            while (line.LineNumber > 0)
            {
                line = line.Snapshot.GetLineFromLineNumber(line.LineNumber - 1);
                text = line.GetText();
                if (!IsBlankLine(text))
                {
                    baseline = line;
                    baselineText = text;
                    return;
                }
            }
            baselineText = line.GetText();
            baseline = line;
        }

        private static bool PythonContentTypePrediciate(ITextSnapshot snapshot)
        {
            return snapshot.ContentType.IsOfType(VSGeneroConstants.ContentType4GL) ||
                   snapshot.ContentType.IsOfType(VSGeneroConstants.ContentTypeINC) ||
                   snapshot.ContentType.IsOfType(VSGeneroConstants.ContentTypePER);
        }

        internal static int? GetLineIndentation(ITextSnapshotLine line, ITextView textView)
        {
            var options = textView.Options;

            ITextSnapshotLine baseline;
            string baselineText;
            SkipPreceedingBlankLines(line, out baselineText, out baseline);

            ITextBuffer targetBuffer = textView.TextBuffer;
            if (!targetBuffer.ContentType.IsOfType(VSGeneroConstants.ContentType4GL) ||
                !targetBuffer.ContentType.IsOfType(VSGeneroConstants.ContentTypeINC) ||
                !targetBuffer.ContentType.IsOfType(VSGeneroConstants.ContentTypePER))
            {
                var match = textView.BufferGraph.MapDownToFirstMatch(line.Start, PointTrackingMode.Positive, PythonContentTypePrediciate, PositionAffinity.Successor);
                if (match == null)
                {
                    return 0;
                }
                targetBuffer = match.Value.Snapshot.TextBuffer;
            }

            var classifier = targetBuffer.GetGeneroClassifier();
            if (classifier == null)
            {
                // workaround debugger canvas bug - they wire our auto-indent provider up to a C# buffer
                // (they query MEF for extensions by hand and filter incorrectly) and we don't have a Python classifier.  
                // So now the user's auto-indent is broken in C# but returning null is better than crashing.
                return null;
            }

            var desiredIndentation = CalculateIndentation(baselineText, baseline, options, classifier, textView);

            var caretLine = textView.Caret.Position.BufferPosition.GetContainingLine();
            // VS will get the white space when the user is moving the cursor or when the user is doing an edit which
            // introduces a new line.  When the user is moving the cursor the caret line differs from the line
            // we're querying.  When editing the lines are the same and so we want to account for the white space of
            // non-blank lines.  An alternate strategy here would be to watch for the edit and fix things up after
            // the fact which is what would happen pre-Dev10 when the language would not get queried for non-blank lines
            // (and is therefore what C# and other languages are doing).
            if (caretLine.LineNumber == line.LineNumber)
            {
                var lineText = caretLine.GetText();
                int indentationUpdate = 0;
                for (int i = textView.Caret.Position.BufferPosition.Position - caretLine.Start; i < lineText.Length; i++)
                {
                    if (lineText[i] == ' ')
                    {
                        indentationUpdate++;
                    }
                    else if (lineText[i] == '\t')
                    {
                        indentationUpdate += textView.Options.GetIndentSize();
                    }
                    else
                    {
                        if (indentationUpdate > desiredIndentation)
                        {
                            // we would dedent this line (e.g. there's a return on the previous line) but the user is
                            // hitting enter with a statement to the right of the caret and they're in the middle of white space.
                            // So we need to instead just maintain the existing indentation level.
                            desiredIndentation = Math.Max(GetIndentation(baselineText, options.GetTabSize()) - indentationUpdate, 0);
                        }
                        else
                        {
                            desiredIndentation -= indentationUpdate;
                        }
                        break;
                    }
                }
            }

            return desiredIndentation;
        }
    }
}
