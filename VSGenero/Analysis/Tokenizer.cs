﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSGenero.Analysis
{
    public class Tokenizer
    {
        private State _state;
        private bool _disableLineFeedLineSeparator = false;
        private SourceCodeKind _kind = SourceCodeKind.AutoDetect;
        private ErrorSink _errors;
        private Severity _indentationInconsistencySeverity;
        private bool _endContinues, _printFunction, _unicodeLiterals, _withStatement;
        private List<int> _newLineLocations;
        private SourceLocation _initialLocation;
        private TextReader _reader;
        private char[] _buffer;
        private bool _multiEolns;
        private int _position, _end, _tokenEnd, _start, _tokenStartIndex, _tokenEndIndex;
        private bool _bufferResized;
        private TokenizerOptions _options;

        private const int EOF = -1;
        private const int MaxIndent = 80;
        private const int DefaultBufferCapacity = 1024;

        private Dictionary<object, NameToken> _names;
        private static object _currentName = new object();

        // precalcuated strings for space indentation strings so we usually don't allocate.
        private static readonly string[] SpaceIndentation, TabIndentation;

        public Tokenizer(ErrorSink errorSink = null, TokenizerOptions options = TokenizerOptions.None)
        {
            errorSink = errorSink ?? ErrorSink.Null;

            _errors = errorSink;
            _state = new State(options);
            _printFunction = false;
            _unicodeLiterals = false;
            _names = new Dictionary<object, NameToken>(128, new TokenEqualityComparer(this));
            _options = options;
        }

        static Tokenizer()
        {
            SpaceIndentation = new String[80];
            for (int i = 0; i < 80; i++)
            {
                SpaceIndentation[i] = new string(' ', i + 1);
            }
            TabIndentation = new String[10];
            for (int i = 0; i < 10; i++)
            {
                TabIndentation[i] = new string('\t', i + 1);
            }
        }

        public bool Verbatim
        {
            get
            {
                return (_options & TokenizerOptions.Verbatim) != 0;
            }
        }

        public void AdjustOptions(TokenizerOptions options)
        {
            _options = options;
        }

        public TokenizerOptions CurrentOptions
        {
            get
            { return _options; }
        }

        /// <summary>
        /// Get all tokens over a block of the stream.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The scanner should return full tokens. If startLocation + length lands in the middle of a token, the full token
        /// should be returned.
        /// </para>
        /// </remarks>
        /// <param name="characterCount">Tokens are read until at least given amount of characters is read or the stream ends.</param>
        /// <returns>A enumeration of tokens.</returns>
        public List<TokenInfo> ReadTokens(int characterCount)
        {
            List<TokenInfo> tokens = new List<TokenInfo>();

            int start = CurrentPosition.Index;

            while (CurrentPosition.Index - start < characterCount)
            {
                TokenInfo token = ReadToken();
                if (token.Category == TokenCategory.EndOfStream)
                {
                    break;
                }
                tokens.Add(token);
            }

            return tokens;
        }

        public object CurrentState
        {
            get
            {
                return _state;
            }
        }

        public SourceLocation CurrentPosition
        {
            get
            {
                return IndexToLocation(CurrentIndex);
            }
        }

        public SourceLocation IndexToLocation(int index)
        {
            int match = _newLineLocations.BinarySearch(index);
            if (match < 0)
            {
                // If our index = -1, it means we're on the first line.
                if (match == -1)
                {
                    return new SourceLocation(index + _initialLocation.Index, _initialLocation.Line, checked(index + _initialLocation.Column));
                }
                // If we couldn't find an exact match for this line number, get the nearest
                // matching line number less than this one
                match = ~match - 1;
            }

            return new SourceLocation(index + _initialLocation.Index, match + 2 + _initialLocation.Line - 1, index - _newLineLocations[match] + _initialLocation.Column);
        }

        internal ErrorSink ErrorSink
        {
            get { return _errors; }
            set
            {
                Contract.Assert(value != null);
                _errors = value;
            }
        }

        internal Severity IndentationInconsistencySeverity
        {
            get { return _indentationInconsistencySeverity; }
            set
            {
                _indentationInconsistencySeverity = value;

                if (value != Severity.Ignore && _state.IndentFormat == null)
                {
                    _state.IndentFormat = new string[MaxIndent];
                }
            }
        }

        public bool IsEndOfFile
        {
            get
            {
                return Peek() == EOF;
            }
        }

        internal IndexSpan TokenSpan
        {
            get
            {
                return new IndexSpan(_tokenStartIndex, _tokenEndIndex - _tokenStartIndex);
            }
        }

        public void Initialize(TextReader sourceUnit)
        {
            Contract.Assert(sourceUnit != null);

            Initialize(null, sourceUnit, SourceLocation.MinValue, DefaultBufferCapacity);
        }

        public void Initialize(object state, TextReader reader, SourceLocation initialLocation)
        {
            Initialize(state, reader, initialLocation, DefaultBufferCapacity);
        }

        public void Initialize(object state, TextReader reader, SourceLocation initialLocation, int bufferCapacity)
        {
            Contract.Assert(reader != null);

            if (state != null)
            {
                if (!(state is State)) throw new ArgumentException("bad state provided");
                _state = new State((State)state, Verbatim);
            }
            else
            {
                _state = new State(_options);
            }

            Debug.Assert(_reader == null, "Must uninitialize tokenizer before reinitializing");
            _reader = reader;

            if (_buffer == null || _buffer.Length < bufferCapacity)
            {
                _buffer = new char[bufferCapacity];
            }

            _newLineLocations = new List<int>();
            _tokenEnd = -1;
            _multiEolns = !_disableLineFeedLineSeparator;
            _initialLocation = initialLocation;

            _tokenEndIndex = -1;
            _tokenStartIndex = 0;

            _start = _end = 0;
            _position = 0;
        }

        public void Uninitialize()
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
            _start = _end = 0;
            _position = 0;
        }

        public static TokenInfo GetTokenInfo(Token token)
        {
            TokenInfo result = new TokenInfo();
            switch (token.Kind)
            {
                case TokenKind.EndOfFile:
                    result.Category = TokenCategory.EndOfStream;
                    break;

                case TokenKind.Comment:
                    result.Category = TokenCategory.Comment;
                    break;

                case TokenKind.Name:
                    result.Category = TokenCategory.Identifier;
                    break;

                case TokenKind.Error:
                    if (token is IncompleteStringErrorToken)
                    {
                        result.Category = TokenCategory.IncompleteMultiLineStringLiteral;
                    }
                    else if (token is IncompleteMultiLineCommentErrorToken)
                    {
                        result.Category = TokenCategory.IncompleteMultiLineComment;
                    }
                    else
                    {
                        result.Category = TokenCategory.Error;
                    }
                    break;

                case TokenKind.Constant:
                    result.Category = (token.Value is string || token.Value is AsciiString) ? TokenCategory.StringLiteral : TokenCategory.NumericLiteral;
                    break;

                case TokenKind.LeftParenthesis:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterStart;
                    break;

                case TokenKind.RightParenthesis:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterEnd;
                    break;

                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightBrace:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces;
                    break;

                case TokenKind.Colon:
                    result.Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Semicolon:
                    result.Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Comma:
                    result.Category = TokenCategory.Delimiter;
                    result.Trigger = TokenTriggers.ParameterNext;
                    break;

                case TokenKind.Dot:
                    result.Category = TokenCategory.Operator;
                    result.Trigger = TokenTriggers.MemberSelect;
                    break;

                case TokenKind.NewLine:
                case TokenKind.NLToken:
                    result.Category = TokenCategory.WhiteSpace;
                    break;

                default:
                    if (token.Kind >= TokenKind.FirstLanguageKeyword && token.Kind <= TokenKind.LastLanguageKeyword)
                    {
                        result.Category = TokenCategory.Keyword;
                        break;
                    }

                    result.Category = TokenCategory.Operator;
                    break;
            }

            return result;
        }

        public TokenInfo ReadToken()
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Uninitialized");
            }

            TokenInfo result = new TokenInfo();
            Token token = GetNextToken();
            result.SourceSpan = new SourceSpan(IndexToLocation(TokenSpan.Start), IndexToLocation(TokenSpan.End));

            switch (token.Kind)
            {
                case TokenKind.EndOfFile:
                    result.Category = TokenCategory.EndOfStream;
                    break;

                case TokenKind.Comment:
                    result.Category = TokenCategory.Comment;
                    break;

                case TokenKind.Name:
                    result.Category = TokenCategory.Identifier;
                    break;

                case TokenKind.Error:
                    if (token is IncompleteStringErrorToken)
                    {
                        result.Category = TokenCategory.IncompleteMultiLineStringLiteral;
                    }
                    else if(token is IncompleteMultiLineCommentErrorToken)
                    {
                        result.Category = TokenCategory.IncompleteMultiLineComment;
                    }
                    else
                    {
                        result.Category = TokenCategory.Error;
                    }
                    break;

                case TokenKind.Constant:
                    result.Category = (token.Value is string || token.Value is AsciiString) ? TokenCategory.StringLiteral : TokenCategory.NumericLiteral;
                    break;

                case TokenKind.LeftParenthesis:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterStart;
                    break;

                case TokenKind.RightParenthesis:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterEnd;
                    break;

                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightBrace:
                    result.Category = TokenCategory.Grouping;
                    result.Trigger = TokenTriggers.MatchBraces;
                    break;

                case TokenKind.Colon:
                    result.Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Semicolon:
                    result.Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Comma:
                    result.Category = TokenCategory.Delimiter;
                    result.Trigger = TokenTriggers.ParameterNext;
                    break;

                case TokenKind.Dot:
                    result.Category = TokenCategory.Operator;
                    result.Trigger = TokenTriggers.MemberSelect;
                    break;

                case TokenKind.NewLine:
                case TokenKind.NLToken:
                    result.Category = TokenCategory.WhiteSpace;
                    break;

                default:
                    if (token.Kind >= TokenKind.FirstLanguageKeyword && token.Kind <= TokenKind.LastLanguageKeyword)
                    {
                        result.Category = TokenCategory.Keyword;
                        break;
                    }

                    result.Category = TokenCategory.Operator;
                    break;
            }

            return result;
        }

        private Token TransformStatementToken(Token token)
        {
            if (GroupingLevel > 0 &&
                (_options & TokenizerOptions.GroupingRecovery) != 0 &&
                _state.GroupingRecovery != null &&
                _state.GroupingRecovery.TokenStart == _tokenStartIndex)
            {

                _state.ParenLevel = _state.BraceLevel = _state.BracketLevel = 0;

                // we can't possibly be in a grouping for real if we saw this token, bail...
                int prevStart = _tokenStartIndex;
                _position = _start;
                SetIndent(_state.GroupingRecovery.Spaces, _state.GroupingRecovery.Whitespace, _state.GroupingRecovery.NoAllocWhiteSpace);
                _tokenStartIndex = _state.GroupingRecovery.NewlineStart;
                _tokenEndIndex = _state.GroupingRecovery.NewlineStart + _state.GroupingRecovery.NewLineKind.GetSize();

                if (Verbatim)
                {
                    // fixup our white space, remove the newline + any indentation from the current whitespace, add the whitespace minus the
                    // newline to the next whitespace
                    int nextWhiteSpaceStart = _state.GroupingRecovery.VerbatimWhiteSpaceLength + _state.GroupingRecovery.NewLineKind.GetSize();
                    _state.NextWhiteSpace.Insert(0, _state.CurWhiteSpace.ToString(nextWhiteSpaceStart, _state.CurWhiteSpace.Length - nextWhiteSpaceStart));
                    _state.CurWhiteSpace.Remove(_state.GroupingRecovery.VerbatimWhiteSpaceLength, _state.CurWhiteSpace.Length - nextWhiteSpaceStart + _state.GroupingRecovery.NewLineKind.GetSize());
                }

                var nlKind = _state.GroupingRecovery.NewLineKind;
                _state.GroupingRecovery = null;
                return NewLineKindToToken(nlKind);
            }

            MarkTokenEnd();
            return token;
        }

        internal bool TryGetTokenString(int len, out string tokenString)
        {
            if (len != TokenLength)
            {
                tokenString = null;
                return false;
            }
            tokenString = GetTokenString();
            return true;
        }

        internal bool PrintFunction
        {
            get
            {
                return _printFunction;
            }
            set
            {
                _printFunction = value;
            }
        }

        internal bool WithStatement
        {
            get
            {
                return _withStatement;
            }
            set
            {
                _withStatement = value;
            }
        }

        internal bool UnicodeLiterals
        {
            get
            {
                return _unicodeLiterals;
            }
            set
            {
                _unicodeLiterals = value;
            }
        }

        /// <summary>
        /// Return the white space proceeding the last fetched token. Returns an empty string if
        /// the tokenizer was not created in verbatim mode.
        /// </summary>
        public string PreceedingWhiteSpace
        {
            get
            {
                if (!Verbatim)
                {
                    return "";
                }
                return _state.CurWhiteSpace.ToString();
            }
        }

        public Token GetNextToken()
        {
            if (Verbatim)
            {
                _state.CurWhiteSpace.Clear();
                if (_state.NextWhiteSpace.Length != 0)
                {
                    // flip to the next white space if we have some...
                    var tmp = _state.CurWhiteSpace;
                    _state.CurWhiteSpace = _state.NextWhiteSpace;
                    _state.NextWhiteSpace = tmp;
                }
            }

            Token result;

            if (_state.PendingDedents != 0)
            {
                if (_state.PendingDedents == -1)
                {
                    _state.PendingDedents = 0;
                    result = Tokens.IndentToken;
                }
                else
                {
                    _state.PendingDedents--;
                    result = Tokens.DedentToken;
                }
            }
            else
            {
                result = Next();
            }

            DumpToken(result);
            return result;
        }

        private Token Next()
        {
            bool at_beginning = AtBeginning;

            if (_state.IncompleteString != null && Peek() != EOF)
            {
                IncompleteString prev = _state.IncompleteString;
                _state.IncompleteString = null;
                return ContinueString(prev.IsSingleTickQuote ? '\'' : '"', prev.IsRaw, prev.IsUnicode, false, prev.IsTripleQuoted, 0);
            }
            else if(_state.MultiLineComment && Peek() != EOF)
            {
                return ReadMultiLineComment();
            }

            DiscardToken();

            int ch = NextChar();

            while (true)
            {
                switch (ch)
                {
                    case EOF:
                        return ReadEof();
                    case '\f':
                        // Ignore form feeds
                        if (Verbatim)
                        {
                            _state.CurWhiteSpace.Append((char)ch);
                        }
                        DiscardToken();
                        ch = NextChar();
                        break;
                    case ' ':
                    case '\t':
                        ch = SkipWhiteSpace(ch, at_beginning);
                        break;

                    case '#':
                        if ((_options & (TokenizerOptions.VerbatimCommentsAndLineJoins | TokenizerOptions.Verbatim)) != 0)
                        {
                            var commentRes = ReadSingleLineComment(out ch);
                            if ((_options & TokenizerOptions.VerbatimCommentsAndLineJoins) == 0)
                            {
                                _state.CurWhiteSpace.Append(commentRes.VerbatimImage);
                                DiscardToken();
                                SeekRelative(+1);
                            }
                            else
                            {
                                return commentRes;
                            }
                        }
                        else
                        {
                            ch = SkipSingleLineComment();
                        }
                        break;

                    case '\\':
                        NewLineKind nlKind;
                        var nextChar = NextChar();
                        if ((nlKind = ReadEolnOpt(nextChar)) != NewLineKind.None)
                        {
                            _newLineLocations.Add(CurrentIndex);

                            if ((_options & TokenizerOptions.VerbatimCommentsAndLineJoins) != 0)
                            {
                                // report the explicit line join
                                MarkTokenEnd();

                                return new VerbatimToken(TokenKind.ExplicitLineJoin, "\\" + nlKind.GetString(), "<explicit line join>");
                            }
                            else
                            {
                                DiscardToken();
                                // discard token '\\<eoln>':
                                if (_state.CurWhiteSpace != null)
                                {
                                    _state.CurWhiteSpace.Append('\\');
                                    _state.CurWhiteSpace.Append(nlKind.GetString());
                                }
                            }

                            ch = NextChar();
                            if (ch == -1)
                            {
                                _endContinues = true;
                            }
                            break;

                        }
                        else
                        {
                            if (nextChar == -1)
                            {
                                _endContinues = true;
                                MarkTokenEnd();
                                return new VerbatimToken(TokenKind.EndOfFile, "\\", "<eof>");
                            }
                            BufferBack();
                            goto default;
                        }

                    case '\"':
                    case '\'':
                        _state.LastNewLine = false;
                        return ReadString((char)ch, false, false, false);

                    case 'u':
                    case 'U':
                        _state.LastNewLine = false;
                        // The u prefix was reintroduced to Python 3.3 in PEP 414
                        //if (_langVersion.Is2x() || _langVersion >= PythonLanguageVersion.V33)
                        //{
                        //    return ReadNameOrUnicodeString();
                        //}
                        return ReadName();
                    case 'r':
                    case 'R':
                        _state.LastNewLine = false;
                        return ReadNameOrRawString();
                    case 'b':
                    case 'B':
                        _state.LastNewLine = false;
                        //if (_langVersion >= PythonLanguageVersion.V26)
                        //{
                        //    return ReadNameOrBytes();
                        //}
                        return ReadName();
                    case '_':
                        _state.LastNewLine = false;
                        return ReadName();

                    case '.':
                        _state.LastNewLine = false;
                        ch = Peek();
                        if (ch >= '0' && ch <= '9')
                        {
                            return ReadFraction();
                        }
                        //else if (ch == '.' && _langVersion.Is3x())
                        //{
                        //    NextChar();
                        //    if (Peek() == '.')
                        //    {
                        //        NextChar();
                        //        MarkTokenEnd();
                        //        return Tokens.Ellipsis;
                        //    }
                        //    else
                        //    {
                        //        BufferBack();
                        //    }
                        //}

                        MarkTokenEnd();

                        return Tokens.DotToken;

                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        _state.LastNewLine = false;
                        return ReadNumber(ch);

                    default:
                        if ((nlKind = ReadEolnOpt(ch)) > 0)
                        {
                            _newLineLocations.Add(CurrentIndex);
                            // token marked by the callee:
                            if (ReadIndentationAfterNewLine(nlKind))
                            {
                                return NewLineKindToToken(nlKind, _state.LastNewLine);
                            }

                            // we're in a grouping, white space is ignored
                            DiscardToken();
                            ch = NextChar();
                            break;
                        }

                        _state.LastNewLine = false;
                        Token res = NextOperator(ch);
                        if (res != null)
                        {
                            if (res is StatementSymbolToken)
                            {
                                return TransformStatementToken(res);
                            }
                            MarkTokenEnd();
                            return res;
                        }

                        if (IsNameStart(ch)) return ReadName();

                        MarkTokenEnd();
                        return BadChar(ch);
                }
            }
        }

        private Token NewLineKindToToken(NewLineKind nlKind, bool lastNewLine = false)
        {
            if (lastNewLine)
            {
                switch (nlKind)
                {
                    case NewLineKind.CarriageReturn: return Tokens.NLTokenCR;
                    case NewLineKind.CarriageReturnLineFeed: return Tokens.NLTokenCRLF;
                    case NewLineKind.LineFeed: return Tokens.NLToken;
                }
            }
            else
            {
                _state.LastNewLine = true;
                switch (nlKind)
                {
                    case NewLineKind.CarriageReturn: return Tokens.NewLineTokenCR;
                    case NewLineKind.CarriageReturnLineFeed: return Tokens.NewLineTokenCRLF;
                    case NewLineKind.LineFeed: return Tokens.NewLineToken;
                }
            }
            throw new InvalidOperationException();
        }

        private int SkipWhiteSpace(int ch, bool atBeginning)
        {
            do
            {
                if (Verbatim)
                {
                    _state.CurWhiteSpace.Append((char)ch);
                }
                ch = NextChar();
            } while (ch == ' ' || ch == '\t');

            BufferBack();

            if (atBeginning && ch != '#' && ch != '\f' && ch != EOF && !IsEoln(ch))
            {
                MarkTokenEnd();
                ReportSyntaxError(BufferTokenSpan, "invalid syntax", ErrorCodes.SyntaxError);
            }

            DiscardToken();
            SeekRelative(+1);
            return ch;
        }

        private int SkipSingleLineComment()
        {
            // do single-line comment:
            int ch = ReadLine();
            MarkTokenEnd();

            // discard token '# ...':
            DiscardToken();
            SeekRelative(+1);

            return ch;
        }

        private Token ReadSingleLineComment(out int ch)
        {
            // do single-line comment:
            ch = ReadLine();
            MarkTokenEnd();

            return new CommentToken(GetTokenString());
        }

        private Token ReadNameOrUnicodeString()
        {
            if (NextChar('\"')) return ReadString('\"', false, true, false);
            if (NextChar('\'')) return ReadString('\'', false, true, false);
            if (NextChar('r') || NextChar('R'))
            {
                if (NextChar('\"')) return ReadString('\"', true, true, false);
                if (NextChar('\'')) return ReadString('\'', true, true, false);
                BufferBack();
            }
            return ReadName();
        }

        private Token ReadNameOrBytes()
        {
            if (NextChar('\"')) return ReadString('\"', false, false, true);
            if (NextChar('\'')) return ReadString('\'', false, false, true);
            if (NextChar('r') || NextChar('R'))
            {
                if (NextChar('\"')) return ReadString('\"', true, false, true);
                if (NextChar('\'')) return ReadString('\'', true, false, true);
                BufferBack();
            }
            return ReadName();
        }

        private Token ReadNameOrRawString()
        {
            bool isBytes = false;
            //if (this._langVersion >= PythonLanguageVersion.V33)
            //{
            //    isBytes = NextChar('b') || NextChar('B');
            //}
            if (NextChar('\"')) return ReadString('\"', true, false, isBytes);
            if (NextChar('\'')) return ReadString('\'', true, false, isBytes);
            return ReadName();
        }

        private Token ReadEof()
        {
            MarkTokenEnd();

            if (/*!_dontImplyDedent && */_state.IndentLevel > 0 && GroupingLevel == 0)
            {
                // before we imply dedents we need to make sure the last thing we returned was
                // a new line.
                if (!_state.LastNewLine)
                {
                    _state.LastNewLine = true;
                    return Tokens.ImpliedNewLineToken;
                }

                // and then go ahead and imply the dedents.
                SetIndent(0, null, null, _position);
                _state.PendingDedents--;
                return Tokens.DedentToken;
            }

            return Tokens.EndOfFileToken;
        }

        private static string AddSlashes(string str)
        {
            StringBuilder result = new StringBuilder(str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                switch (str[i])
                {
                    case '\a': result.Append("\\a"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    case '\v': result.Append("\\v"); break;
                    default: result.Append(str[i]); break;
                }
            }

            return result.ToString();
        }

        private static ErrorToken BadChar(int ch)
        {
            Debug.Assert(new string((char)ch, 1)[0] == ch);
            return new ErrorToken(AddSlashes(((char)ch).ToString()), new string((char)ch, 1));
        }

        private static bool IsNameStart(int ch)
        {
            return Char.IsLetter((char)ch) || ch == '_';
        }

        private static bool IsNamePart(int ch)
        {
            return Char.IsLetterOrDigit((char)ch) || ch == '_';
        }

        private Token ReadString(char quote, bool isRaw, bool isUni, bool isBytes)
        {
            int sadd = 0;
            bool isTriple = false;

            if (NextChar(quote))
            {
                if (NextChar(quote))
                {
                    isTriple = true; sadd += 3;
                }
                else
                {
                    BufferBack();
                    sadd++;
                }
            }
            else
            {
                sadd++;
            }

            if (isRaw) sadd++;
            if (isUni) sadd++;
            if (isBytes) sadd++;

            return ContinueString(quote, isRaw, isUni, isBytes, isTriple, sadd);
        }

        private Token ReadMultiLineComment()
        {
            NewLineKind nlKind;
            for (; ; )
            {
                int ch = NextChar();

                if (ch == EOF)
                {
                    BufferBack();
                    MarkTokenEnd();
                    UnexpectedEndOfComment();
                    string incompleteContents = GetTokenString();

                    _state.MultiLineComment = true;
                    return new IncompleteMultiLineCommentErrorToken("<eof> while reading comment", incompleteContents);
                }
                else if (ch == '}')
                {
                    break;
                }
                else if ((nlKind = ReadEolnOpt(ch)) > 0)//(ch == '\\')
                {
                    ch = NextChar();

                    if (ch == EOF)
                    {
                        BufferBack();
                        MarkTokenEnd();
                        MarkTokenEnd();
                        UnexpectedEndOfComment();

                        string incompleteContents = GetTokenString();

                        _state.MultiLineComment = true;

                        return new IncompleteMultiLineCommentErrorToken("<eof> while reading comment", incompleteContents);
                    }
                    else if ((nlKind = ReadEolnOpt(ch)) > 0)
                    {
                        _newLineLocations.Add(CurrentIndex);

                        // skip \<eoln> unless followed by EOF:
                        if (Peek() == EOF)
                        {
                            MarkTokenEnd();

                            // incomplete string in the form "abc\

                            string incompleteContents = GetTokenString();

                            _state.MultiLineComment = true;
                            UnexpectedEndOfComment();
                            return new IncompleteMultiLineCommentErrorToken("<eof> while reading comment", incompleteContents);
                        }

                    }
                    else if (ch != '}')
                    {
                        BufferBack();
                    }
                }
            }

            MarkTokenEnd();
            _state.MultiLineComment = false;

            return new CommentToken(GetTokenString());
        }

        private Token ContinueString(char quote, bool isRaw, bool isUnicode, bool isBytes, bool isTriple, int startAdd)
        {
            // PERF: Might be nice to have this not need to get the whole token (which requires a buffer >= in size to the
            // length of the string) and instead build up the string via pieces.  Currently on files w/ large doc strings we
            // are forced to grow our buffer.

            int end_add = 0;
            NewLineKind nlKind;

            for (; ; )
            {
                int ch = NextChar();

                if (ch == EOF)
                {
                    BufferBack();

                    if (isTriple)
                    {
                        // CPython reports the multi-line string error as if it is a single line
                        // ending at the last char in the file.

                        MarkTokenEnd();

                        ReportSyntaxError(new IndexSpan(_tokenEndIndex, 0), "EOF while scanning triple-quoted string", ErrorCodes.SyntaxError | ErrorCodes.IncompleteToken);
                    }
                    else
                    {
                        MarkTokenEnd();
                    }

                    UnexpectedEndOfString(isTriple, isTriple);
                    string incompleteContents = GetTokenString();

                    _state.IncompleteString = new IncompleteString(quote == '\'', isRaw, isUnicode, isTriple);
                    return new IncompleteStringErrorToken("<eof> while reading string", incompleteContents);
                }
                else if (ch == quote && _position > 1 && _buffer[_position - 2] != '\\')
                {

                    if (isTriple)
                    {
                        if (NextChar(quote) && NextChar(quote))
                        {
                            end_add += 3;
                            break;
                        }
                    }
                    else
                    {
                        end_add++;
                        break;
                    }

                }
                else if ((nlKind = ReadEolnOpt(ch)) > 0)//(ch == '\\')
                {
                    ch = NextChar();

                    if (ch == EOF)
                    {
                        BufferBack();

                        MarkTokenEnd();
                        UnexpectedEndOfString(isTriple, isTriple);

                        string incompleteContents = GetTokenString();

                        _state.IncompleteString = new IncompleteString(quote == '\'', isRaw, isUnicode, isTriple);

                        return new IncompleteStringErrorToken("<eof> while reading string", incompleteContents);
                    }
                    else if ((nlKind = ReadEolnOpt(ch)) > 0)
                    {
                        _newLineLocations.Add(CurrentIndex);

                        // skip \<eoln> unless followed by EOF:
                        if (Peek() == EOF)
                        {
                            MarkTokenEnd();

                            // incomplete string in the form "abc\

                            string incompleteContents = GetTokenString();

                            _state.IncompleteString = new IncompleteString(quote == '\'', isRaw, isUnicode, isTriple);
                            UnexpectedEndOfString(isTriple, true);
                            return new IncompleteStringErrorToken("<eof> while reading string", incompleteContents);
                        }

                    }
                    else if (ch != quote && ch != '\\')
                    {
                        BufferBack();
                    }

                }
                //else if ((nlKind = ReadEolnOpt(ch)) > 0)
                //{
                //    _newLineLocations.Add(CurrentIndex);
                //    if (!isTriple)
                //    {
                //        // backup over the eoln:

                //        MarkTokenEnd();
                //        UnexpectedEndOfString(isTriple, false);

                //        string incompleteContents = GetTokenString();

                //        return new IncompleteStringErrorToken((quote == '"') ? "NEWLINE in double-quoted string" : "NEWLINE in single-quoted string", incompleteContents);
                //    }
                //}
            }

            MarkTokenEnd();

            return MakeStringToken(quote, isRaw, isUnicode, isBytes, isTriple, _start + startAdd, TokenLength - startAdd - end_add);
        }

        private Token MakeStringToken(char quote, bool isRaw, bool isUnicode, bool isBytes, bool isTriple, int start, int length)
        {
            bool makeUnicode = false;
            //if (isUnicode)
            //{
            //    makeUnicode = true;
            //}
            //else if (isBytes)
            //{
            //    makeUnicode = false;
            //}
            //else
            //{
            //    makeUnicode = _langVersion.Is3x() || UnicodeLiterals;
            //}

            if (makeUnicode)
            {
                string contents;
                try
                {
                    contents = LiteralParser.ParseString(_buffer, start, length, isRaw, true, !_disableLineFeedLineSeparator);
                }
                catch (DecoderFallbackException e)
                {
                    _errors.Add(e.Message, _newLineLocations.ToArray(), _tokenStartIndex, _tokenEndIndex, ErrorCodes.SyntaxError, Severity.Error);
                    contents = "";
                }

                if (Verbatim)
                {
                    return new VerbatimUnicodeStringToken(contents, GetTokenString());
                }
                return new UnicodeStringToken(contents);
            }
            else
            {
                var data = LiteralParser.ParseBytes(_buffer, start, length, isRaw, !_disableLineFeedLineSeparator);
                if (data.Count == 0)
                {
                    if (Verbatim)
                    {
                        return new VerbatimConstantValueToken(new AsciiString(new byte[0], ""), GetTokenString());
                    }
                    return new ConstantValueToken(new AsciiString(new byte[0], ""));
                }

                byte[] bytes = new byte[data.Count];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)data[i];
                }

                if (Verbatim)
                {
                    return new VerbatimConstantValueToken(new AsciiString(bytes, new String(data.ToArray())), GetTokenString());
                }
                return new ConstantValueToken(new AsciiString(bytes, new String(data.ToArray())));
            }
        }

        private void UnexpectedEndOfString(bool isTriple, bool isIncomplete)
        {
            string message = isTriple ? "EOF while scanning triple-quoted string" : "EOL while scanning single-quoted string";
            int error = isIncomplete ? ErrorCodes.SyntaxError | ErrorCodes.IncompleteToken : ErrorCodes.SyntaxError;

            ReportSyntaxError(BufferTokenSpan, message, error);
        }

        private void UnexpectedEndOfComment()
        {
            string message = "EOL while scanning multi-line comment";
            int error = ErrorCodes.IncompleteToken;
            ReportSyntaxError(BufferTokenSpan, message, error);
        }

        private Token ReadNumber(int start)
        {
            int b = 10;
            //if (start == '0')
            //{
            //    if (NextChar('x') || NextChar('X'))
            //    {
            //        return ReadHexNumber();
            //    }
            //    else if (_langVersion >= PythonLanguageVersion.V26)
            //    {
            //        if ((NextChar('b') || NextChar('B')))
            //        {
            //            return ReadBinaryNumber();
            //        }
            //        else if (NextChar('o') || NextChar('O'))
            //        {
            //            return ReadOctalNumber();
            //        }
            //    }

            //    b = 8;
            //}

            while (true)
            {
                int ch = NextChar();

                switch (ch)
                {
                    case '.':
                        return ReadFraction();

                    case 'e':
                    case 'E':
                        return ReadExponent();

                    //case 'j':
                    //case 'J':
                    //    MarkTokenEnd();

                    //    // TODO: parse in place
                    //    if (Verbatim)
                    //    {
                    //        string tokenStr = GetTokenString();
                    //        return new VerbatimConstantValueToken(LiteralParser.ParseImaginary(tokenStr), tokenStr);
                    //    }
                    //    return new ConstantValueToken(LiteralParser.ParseImaginary(GetTokenString()));

                    //case 'l':
                    //case 'L':
                    //    {
                    //        MarkTokenEnd();

                    //        if (_langVersion.Is3x())
                    //        {
                    //            ReportSyntaxError(new IndexSpan(_tokenEndIndex - 1, 1), "invalid token", ErrorCodes.SyntaxError);
                    //        }
                    //        string tokenStr = GetTokenString();
                    //        try
                    //        {
                    //            // TODO: parse in place
                    //            if (Verbatim)
                    //            {
                    //                return new VerbatimConstantValueToken(LiteralParser.ParseBigInteger(tokenStr, b), tokenStr);
                    //            }

                    //            return new ConstantValueToken(LiteralParser.ParseBigInteger(tokenStr, b));
                    //        }
                    //        catch (ArgumentException e)
                    //        {
                    //            return new ErrorToken(e.Message, tokenStr);
                    //        }
                    //    }
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        break;

                    default:
                        BufferBack();
                        MarkTokenEnd();

                        string image = GetTokenString();
                        object val = ParseInteger(GetTokenString(), b);
                        //if (b == 8 && _langVersion.Is3x() && (!(val is int) || !((int)val == 0)))
                        //{
                        //    ReportSyntaxError(BufferTokenSpan, "invalid token", ErrorCodes.SyntaxError);
                        //}

                        if (Verbatim)
                        {
                            return new VerbatimConstantValueToken(val, image);
                        }
                        // TODO: parse in place
                        return new ConstantValueToken(val);
                }
            }
        }

        private Token ReadFraction()
        {
            while (true)
            {
                int ch = NextChar();

                switch (ch)
                {
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        break;

                    case 'e':
                    case 'E':
                        return ReadExponent();

                    default:
                        BufferBack();
                        MarkTokenEnd();

                        // TODO: parse in place
                        if (Verbatim)
                        {
                            string tokenStr = GetTokenString();
                            return new VerbatimConstantValueToken(ParseFloat(tokenStr), tokenStr);
                        }
                        return new ConstantValueToken(ParseFloat(GetTokenString()));
                }
            }
        }

        private Token ReadExponent()
        {
            int ch = NextChar();

            if (ch == '-' || ch == '+')
            {
                ch = NextChar();
            }

            while (true)
            {
                switch (ch)
                {
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        ch = NextChar();
                        break;

                    default:
                        BufferBack();
                        MarkTokenEnd();

                        // TODO: parse in place
                        if (Verbatim)
                        {
                            string tokenStr = GetTokenString();
                            return new VerbatimConstantValueToken(ParseFloat(tokenStr), tokenStr);
                        }
                        return new ConstantValueToken(ParseFloat(GetTokenString()));
                }
            }
        }

        private Token ReadName()
        {
            int ch;
            BufferBack();
            ch = NextChar();

            while (IsNamePart(ch))
            {
                ch = NextChar();
            }
            BufferBack();

            MarkTokenEnd();

            string name = GetTokenString();

            Token tok = Tokens.GetToken(name);
            if (tok == null)
            {
                NameToken token;
                if (!_names.TryGetValue(name, out token))
                {
                    token = _names[name] = new NameToken(name);
                }
                tok = token;
            }
            return tok;
        }

        private Token NextOperator(int ch)
        {
            switch (ch)
            {
                case '+':
                    return Tokens.AddToken;
                case '-':
                    if (NextChar('-'))
                    {
                        if ((_options & (TokenizerOptions.VerbatimCommentsAndLineJoins | TokenizerOptions.Verbatim)) != 0)
                        {
                            var commentRes = ReadSingleLineComment(out ch);
                            if ((_options & TokenizerOptions.VerbatimCommentsAndLineJoins) == 0)
                            {
                                _state.CurWhiteSpace.Append(commentRes.VerbatimImage);
                                DiscardToken();
                                SeekRelative(+1);
                            }
                            else
                            {
                                return commentRes;
                            }
                        }
                        else
                        {
                            ch = SkipSingleLineComment();
                        }
                        break;
                    }
                    return Tokens.SubtractToken;
                case '/':
                    return Tokens.DivideToken;
                case '*':
                    if (NextChar('*'))
                    {
                        return Tokens.PowerToken;
                    }
                    return Tokens.MultiplyToken;
                case '|':
                    if (NextChar('|'))
                    {
                        return Tokens.ConcatToken;
                    }
                    break;
                case '<':
                    if (NextChar('>'))
                    {
                        return Tokens.NotEqualsLTGTToken;
                    }
                    if (NextChar('='))
                    {
                        return Tokens.LessThanEqualToken;
                    }
                    return Tokens.LessThanToken;
                case '>':
                    if (NextChar('='))
                    {
                        return Tokens.GreaterThanEqualToken;
                    }
                    return Tokens.GreaterThanToken;
                case '!':
                    if (NextChar('='))
                    {
                        return Tokens.NotEqualsToken;
                    }
                    return BadChar(ch);
                case '(':
                    _state.ParenLevel++;
                    return Tokens.LeftParenthesisToken;
                case ')':
                    if (_state.ParenLevel != 0)
                    {
                        _state.ParenLevel--;
                    }
                    return Tokens.RightParenthesisToken;
                case '[':
                    _state.BracketLevel++;
                    return Tokens.LeftBracketToken;
                case ']':
                    if (_state.BracketLevel != 0)
                    {
                        _state.BracketLevel--;
                    }
                    return Tokens.RightBracketToken;
                case '{':
                    //_state.BraceLevel++;
                    //return Tokens.LeftBraceToken;
                    return ReadMultiLineComment();
                // We're handling the multi-line comment terminator ('}') in it's own method
                //case '}':
                //    if (_state.BraceLevel != 0)
                //    {
                //        _state.BraceLevel--;
                //    }
                //    return Tokens.RightBraceToken;
                case ',':
                    return Tokens.CommaToken;
                case ':':
                    if (NextChar('='))
                    {
                        return Tokens.AssignToken;
                    }
                    return Tokens.ColonToken;
                case ';':
                    return Tokens.SemicolonToken;
                case '=':
                    if (NextChar('='))
                    {
                        return Tokens.DoubleEqualsToken;
                    }
                    return Tokens.EqualsToken;
                case '&':
                    return Tokens.AmpersandToken;
            }

            return null;
        }

        public int GroupingLevel
        {
            get
            {
                return _state.ParenLevel + _state.BraceLevel + _state.BracketLevel;
            }
        }

        /// <summary>
        /// True if the last characters in the buffer are a backslash followed by a new line indicating
        /// that their is an incompletement statement which needs further input to complete.
        /// </summary>
        public bool EndContinues
        {
            get
            {
                return _endContinues;
            }
        }

        private static void AppendSpace(ref string curWhiteSpace, ref StringBuilder constructedWhiteSpace, ref bool? isSpace)
        {
            if (constructedWhiteSpace == null)
            {
                if (isSpace == null)
                {
                    isSpace = true;
                    curWhiteSpace = SpaceIndentation[0];
                }
                else if (isSpace.Value && curWhiteSpace.Length < SpaceIndentation.Length)
                {
                    curWhiteSpace = SpaceIndentation[curWhiteSpace.Length];
                }
                else
                {
                    // we're mixed tabs/spaces or we have run out of space
                    constructedWhiteSpace = new StringBuilder();
                    constructedWhiteSpace.Append(curWhiteSpace);
                    constructedWhiteSpace.Append(' ');
                }
            }
            else
            {
                constructedWhiteSpace.Append(' ');
            }
        }

        private static void AppendTab(ref string curWhiteSpace, ref StringBuilder constructedWhiteSpace, ref bool? isSpace)
        {
            if (constructedWhiteSpace == null)
            {
                if (isSpace == null)
                {
                    isSpace = false;
                    curWhiteSpace = TabIndentation[0];
                }
                else if (!isSpace.Value && curWhiteSpace.Length < TabIndentation.Length)
                {
                    curWhiteSpace = TabIndentation[curWhiteSpace.Length];
                }
                else
                {
                    // we're mixed tabs/spaces or we have run out of space
                    constructedWhiteSpace = new StringBuilder();
                    constructedWhiteSpace.Append(curWhiteSpace);
                    constructedWhiteSpace.Append('\t');
                }
            }
            else
            {
                constructedWhiteSpace.Append('\t');
            }
        }

        // This is another version of ReadNewline with nearly identical semantics. The difference is
        // that checks are made to see that indentation is used consistently. This logic is in a
        // duplicate method to avoid inflicting the overhead of the extra logic when we're not making
        // the checks.
        /// <summary>
        /// Reads the white space after a new line until we get to the next level of indentation
        /// or a otherwise hit a token which should be returned (any other token if we're in a grouping,
        /// or a comment token if we're in verbatim mode).
        /// 
        /// Returns true if we should return the new line token which kicked this all off.  Returns false
        /// if we should continue processing the current token.
        /// </summary>
        private bool ReadIndentationAfterNewLine(NewLineKind startingKind)
        {
            // Keep track of the indentation format for the current line
            StringBuilder sb = null;                    // the white space we've encounted after the new line if it's mixed tabs/spaces or is an unreasonable size.
            string noAllocWhiteSpace = String.Empty;    // the white space we've encountered after the newline assuming it's a reasonable sized run of all spaces or tabs
            bool? isSpace = null;                       // the current mix of whitespace, null = nothing yet, true = space, false = tab

            int spaces = 0;
            int indentStart = CurrentIndex;
            while (true)
            {
                int ch = NextChar();

                switch (ch)
                {
                    case ' ':
                        if (Verbatim)
                        {
                            _state.NextWhiteSpace.Append((char)ch);
                        }
                        spaces += 1;
                        AppendSpace(ref noAllocWhiteSpace, ref sb, ref isSpace);
                        break;
                    case '\t':
                        if (Verbatim)
                        {
                            _state.NextWhiteSpace.Append((char)ch);
                        }
                        spaces += 8 - (spaces % 8);
                        AppendTab(ref noAllocWhiteSpace, ref sb, ref isSpace);
                        break;
                    case '\f':
                        if (Verbatim)
                        {
                            _state.NextWhiteSpace.Append((char)ch);
                        }
                        spaces = 0;
                        if (sb == null)
                        {
                            sb = new StringBuilder();
                            sb.Append(noAllocWhiteSpace);
                        }
                        sb.Append('\f');
                        break;

                    case '#':

                        if ((_options & TokenizerOptions.VerbatimCommentsAndLineJoins) != 0)
                        {
                            BufferBack();
                            MarkTokenEnd();
                            return true;
                        }
                        else if ((_options & TokenizerOptions.Verbatim) != 0)
                        {
                            BufferBack();
                            DiscardToken();

                            var commentRes = ReadSingleLineComment(out ch);
                            _state.NextWhiteSpace.Append(commentRes.VerbatimImage);
                            DiscardToken();
                            //SeekRelative(+1);
                        }
                        else
                        {
                            ch = ReadLine();
                            break;
                        }
                        break;
                    default:
                        BufferBack();

                        if (GroupingLevel > 0)
                        {
                            int startingWhiteSpace = 0;
                            if (Verbatim)
                            {
                                // we're not producing a new line after all...  All of the white space
                                // we collected goes to the current token, including the new line token
                                // that we're not producing.
                                startingWhiteSpace = _state.CurWhiteSpace.Length;
                                _state.CurWhiteSpace.Append(startingKind.GetString());
                                _state.CurWhiteSpace.Append(_state.NextWhiteSpace);
                                _state.NextWhiteSpace.Clear();
                            }
                            if ((_options & TokenizerOptions.GroupingRecovery) != 0)
                            {
                                int tokenEnd = System.Math.Min(_position, _end);
                                int tokenLength = tokenEnd - _start;

                                _state.GroupingRecovery = new GroupingRecovery(
                                    startingKind,
                                    noAllocWhiteSpace,
                                    spaces,
                                    sb,
                                    _tokenStartIndex,
                                    startingWhiteSpace,
                                    _tokenStartIndex + tokenLength
                                );
                            }
                            return false;
                        }
                        _state.GroupingRecovery = null;
                        MarkTokenEnd();

                        // We've captured a line of significant identation (i.e. not pure whitespace).
                        // Check that any of this indentation that's in common with the current indent
                        // level is constructed in exactly the same way (i.e. has the same mix of spaces
                        // and tabs etc.).
                        if (IndentationInconsistencySeverity != Severity.Ignore)
                        {
                            CheckIndent(sb, noAllocWhiteSpace);
                        }

                        // if there's a blank line then we don't want to mess w/ the
                        // indentation level - Python says that blank lines are ignored.
                        // And if we're the last blank line in a file we don't want to
                        // increase the new indentation level.
                        if (ch == EOF)
                        {
                            if (spaces < _state.Indent[_state.IndentLevel])
                            {
                                if (_kind == SourceCodeKind.InteractiveCode ||
                                    _kind == SourceCodeKind.Statements)
                                {
                                    SetIndent(spaces, sb, noAllocWhiteSpace, indentStart);
                                }
                                else
                                {
                                    DoDedent(spaces, _state.Indent[_state.IndentLevel]);
                                }
                            }
                        }
                        else if (ch != '\n' && ch != '\r')
                        {
                            SetIndent(spaces, sb, noAllocWhiteSpace, indentStart);
                        }

                        return true;
                }
            }
        }

        private static int PreviousIndentLength(object previousIndent)
        {
            string prevStr = previousIndent as string;
            if (prevStr != null)
            {
                return prevStr.Length;
            }

            return ((StringBuilder)previousIndent).Length;
        }

        private void CheckIndent(StringBuilder sb, string noAllocWhiteSpace)
        {
            if (_state.Indent[_state.IndentLevel] > 0)
            {
                var previousIndent = _state.IndentFormat[_state.IndentLevel];
                int checkLength;
                if (sb == null)
                {
                    checkLength = previousIndent.Length < noAllocWhiteSpace.Length ? previousIndent.Length : noAllocWhiteSpace.Length;
                }
                else
                {
                    checkLength = previousIndent.Length < sb.Length ? previousIndent.Length : sb.Length;
                }
                for (int i = 0; i < checkLength; i++)
                {
                    bool neq;
                    if (sb == null)
                    {
                        neq = noAllocWhiteSpace[i] != previousIndent[i];
                    }
                    else
                    {
                        neq = sb[i] != previousIndent[i];
                    }
                    if (neq)
                    {
                        SourceLocation eoln_token_end = BufferTokenEnd;

                        // We've hit a difference in the way we're indenting, report it.
                        _errors.Add("inconsistent whitespace",
                            this._newLineLocations.ToArray(),
                            _tokenStartIndex + 1,
                            _tokenEndIndex,
                            ErrorCodes.TabError, _indentationInconsistencySeverity
                        );
                    }
                }
            }
        }

        private void SetIndent(int spaces, StringBuilder chars, string noAllocWhiteSpace, int indentStart = -1)
        {
            int current = _state.Indent[_state.IndentLevel];
            if (spaces == current)
            {
                return;
            }
            else if (spaces > current)
            {
                _state.Indent[++_state.IndentLevel] = spaces;
                if (_state.IndentFormat != null)
                {
                    if (chars != null)
                    {
                        _state.IndentFormat[_state.IndentLevel] = chars.ToString();
                    }
                    else
                    {
                        _state.IndentFormat[_state.IndentLevel] = noAllocWhiteSpace;
                    }
                }
                _state.PendingDedents = -1;
                return;
            }
            else
            {
                current = DoDedent(spaces, current);

                //if (spaces != current && indentStart != -1)
                //{
                //    ReportSyntaxError(
                //        new IndexSpan(indentStart, spaces),
                //        "unindent does not match any outer indentation level", ErrorCodes.IndentationError);
                //}
            }
        }

        private int DoDedent(int spaces, int current)
        {
            while (spaces < current)
            {
                _state.IndentLevel -= 1;
                _state.PendingDedents += 1;
                current = _state.Indent[_state.IndentLevel];
            }
            return current;
        }

        private object ParseInteger(string s, int radix)
        {
            try
            {
                return LiteralParser.ParseInteger(s, radix);
            }
            catch (ArgumentException e)
            {
                ReportSyntaxError(BufferTokenSpan, e.Message, ErrorCodes.SyntaxError);
                return null;
            }
        }

        private object ParseFloat(string s)
        {
            try
            {
                return LiteralParser.ParseFloat(s);
            }
            catch (Exception e)
            {
                ReportSyntaxError(BufferTokenSpan, e.Message, ErrorCodes.SyntaxError);
                return 0.0;
            }
        }

        private void ReportSyntaxError(IndexSpan span, string message, int errorCode)
        {
            _errors.Add(message, _newLineLocations.ToArray(), span.Start, span.End, errorCode, Severity.FatalError);
        }

        [Conditional("DUMP_TOKENS")]
        private static void DumpToken(Token token)
        {
            Console.WriteLine("{0} `{1}`", token.Kind, token.Image.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
        }

        public int[] GetLineLocations()
        {
            return _newLineLocations.ToArray();
        }

        #region Buffer Access

        private string GetTokenSubstring(int offset)
        {
            return GetTokenSubstring(offset, _tokenEnd - _start - offset);
        }

        private string GetTokenSubstring(int offset, int length)
        {
            Debug.Assert(_tokenEnd != -1, "Token end not marked");
            Debug.Assert(offset >= 0 && offset <= _tokenEnd - _start && length >= 0 && length <= _tokenEnd - _start - offset);

            return new String(_buffer, _start + offset, length);
        }

        [Conditional("DEBUG")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        private void CheckInvariants()
        {
            Debug.Assert(_buffer.Length >= 1);

            // _start == _end when discarding token and at beginning, when == 0
            Debug.Assert(_start >= 0 && _start <= _end);

            Debug.Assert(_end >= 0 && _end <= _buffer.Length);

            // position beyond _end means we are reading EOFs:
            Debug.Assert(_position >= _start);
            Debug.Assert(_tokenEnd >= -1 && _tokenEnd <= _end);
        }

        private int Peek()
        {
            if (_position >= _end)
            {
                RefillBuffer();

                // eof:
                if (_position >= _end)
                {
                    return EOF;
                }
            }

            Debug.Assert(_position < _end);

            return _buffer[_position];
        }

        private int ReadLine()
        {
            int ch;
            do { ch = NextChar(); } while (ch != EOF && !IsEoln(ch));
            BufferBack();
            return ch;
        }

        private void MarkTokenEnd()
        {
            CheckInvariants();

            _tokenEnd = System.Math.Min(_position, _end);
            int token_length = _tokenEnd - _start;

            _tokenEndIndex = _tokenStartIndex + token_length;

            DumpToken();

            CheckInvariants();
        }

        [Conditional("DUMP_TOKENS")]
        private void DumpToken()
        {
            Console.WriteLine("--> `{0}` {1}", GetTokenString().Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"), TokenSpan);
        }

        private void BufferBack()
        {
            SeekRelative(-1);
        }

        internal string GetTokenString()
        {
            return new String(_buffer, _start, _tokenEnd - _start);
        }

        private int TokenLength
        {
            get
            {
                return _tokenEnd - _start;
            }
        }

        private void SeekRelative(int disp)
        {
            CheckInvariants();
            Debug.Assert(disp >= _start - _position);
            // no upper limit, we can seek beyond end in which case we are reading EOFs

            _position += disp;

            CheckInvariants();
        }

        private SourceLocation BufferTokenEnd
        {
            get
            {
                return IndexToLocation(_tokenEndIndex);
            }
        }

        private IndexSpan BufferTokenSpan
        {
            get
            {
                return new IndexSpan(_tokenStartIndex, _tokenEndIndex - _tokenStartIndex);
            }
        }

        private bool NextChar(int ch)
        {
            CheckInvariants();
            if (Peek() == ch)
            {
                _position++;
                CheckInvariants();
                return true;
            }
            else
            {
                return false;
            }
        }

        private int NextChar()
        {
            int result = Peek();
            _position++;
            return result;
        }

        private bool AtBeginning
        {
            get
            {
                return _position == 0 && !_bufferResized;
            }
        }

        private int CurrentIndex
        {
            get
            {
                return _tokenStartIndex + Math.Min(_position, _end) - _start;
            }
        }

        private void DiscardToken()
        {
            CheckInvariants();

            // no token marked => mark it now:
            if (_tokenEnd == -1) MarkTokenEnd();

            // the current token's end is the next token's start:
            _start = _tokenEnd;
            _tokenStartIndex = _tokenEndIndex;
            _tokenEnd = -1;
#if DEBUG
            _tokenEndIndex = -1;
#endif
            CheckInvariants();
        }


        private NewLineKind ReadEolnOpt(int current)
        {
            if (current == '\n')
            {
                return NewLineKind.LineFeed;
            }

            if (current == '\r' && _multiEolns)
            {
                if (Peek() == '\n')
                {
                    SeekRelative(+1);
                    return NewLineKind.CarriageReturnLineFeed;
                }
                return NewLineKind.CarriageReturn;
            }

            return NewLineKind.None;
        }

        private bool IsEoln(int current)
        {
            if (current == '\n') return true;

            if (current == '\r' && _multiEolns)
            {
                if (Peek() == '\n')
                {
                    return true;
                }

                return true;
            }

            return false;
        }

        private void RefillBuffer()
        {
            if (_end == _buffer.Length)
            {
                int new_size = System.Math.Max(System.Math.Max((_end - _start) * 2, _buffer.Length), _position);
                ResizeInternal(ref _buffer, new_size, _start, _end - _start);
                _end -= _start;
                _position -= _start;
                _tokenEnd = -1;
                _start = 0;
                _bufferResized = true;
            }

            // make the buffer full:
            int count = _reader.Read(_buffer, _end, _buffer.Length - _end);
            _end += count;

            ClearInvalidChars();
        }

        /// <summary>
        /// Resizes an array to a speficied new size and copies a portion of the original array into its beginning.
        /// </summary>
        private static void ResizeInternal(ref char[] array, int newSize, int start, int count)
        {
            Debug.Assert(array != null && newSize > 0 && count >= 0 && newSize >= count && start >= 0);

            char[] result = (newSize != array.Length) ? new char[newSize] : array;

            Buffer.BlockCopy(array, start * sizeof(char), result, 0, count * sizeof(char));

            array = result;
        }

        [Conditional("DEBUG")]
        private void ClearInvalidChars()
        {
            for (int i = 0; i < _start; i++) _buffer[i] = '\0';
            for (int i = _end; i < _buffer.Length; i++) _buffer[i] = '\0';
        }

        #endregion

        [Serializable]
        class IncompleteString : IEquatable<IncompleteString>
        {
            public readonly bool IsRaw, IsUnicode, IsTripleQuoted, IsSingleTickQuote;

            public IncompleteString(bool isSingleTickQuote, bool isRaw, bool isUnicode, bool isTriple)
            {
                IsRaw = isRaw;
                IsUnicode = isUnicode;
                IsTripleQuoted = isTriple;
                IsSingleTickQuote = isSingleTickQuote;
            }

            public override bool Equals(object obj)
            {
                IncompleteString oth = obj as IncompleteString;
                if (oth != null)
                {
                    return Equals(oth);
                }
                return false;
            }

            public override int GetHashCode()
            {
                return (IsRaw ? 0x01 : 0) |
                    (IsUnicode ? 0x02 : 0) |
                    (IsTripleQuoted ? 0x04 : 0) |
                    (IsSingleTickQuote ? 0x08 : 0);
            }

            public static bool operator ==(IncompleteString left, IncompleteString right)
            {
                if ((object)left == null) return (object)right == null;

                return left.Equals(right);
            }

            public static bool operator !=(IncompleteString left, IncompleteString right)
            {
                return !(left == right);
            }

            #region IEquatable<State> Members

            public bool Equals(IncompleteString other)
            {
                if (other == null)
                {
                    return false;
                }

                return IsRaw == other.IsRaw &&
                    IsUnicode == other.IsUnicode &&
                    IsTripleQuoted == other.IsTripleQuoted &&
                    IsSingleTickQuote == other.IsSingleTickQuote;
            }

            #endregion
        }

        [Serializable]
        struct State : IEquatable<State>
        {
            // indentation state
            public int[] Indent;
            public int IndentLevel;
            public int PendingDedents;
            public bool LastNewLine;        // true if the last token we emitted was a new line.
            public IncompleteString IncompleteString;
            public bool MultiLineComment;

            // Indentation state used only when we're reporting on inconsistent identation format.
            public string[] IndentFormat;

            // grouping state
            public int ParenLevel, BraceLevel, BracketLevel;

            // white space tracking
            public StringBuilder CurWhiteSpace;
            public StringBuilder NextWhiteSpace;
            public GroupingRecovery GroupingRecovery;

            public State(State state, bool verbatim)
            {
                Indent = (int[])state.Indent.Clone();
                LastNewLine = state.LastNewLine;
                BracketLevel = state.BraceLevel;
                ParenLevel = state.ParenLevel;
                BraceLevel = state.BraceLevel;
                PendingDedents = state.PendingDedents;
                IndentLevel = state.IndentLevel;
                IndentFormat = (state.IndentFormat != null) ? (string[])state.IndentFormat.Clone() : null;
                IncompleteString = state.IncompleteString;
                MultiLineComment = state.MultiLineComment;
                if (verbatim)
                {
                    CurWhiteSpace = new StringBuilder(state.CurWhiteSpace.ToString());
                    NextWhiteSpace = new StringBuilder(state.NextWhiteSpace.ToString());
                }
                else
                {
                    CurWhiteSpace = null;
                    NextWhiteSpace = null;
                }
                GroupingRecovery = null;
            }

            public State(TokenizerOptions options)
            {
                Indent = new int[MaxIndent]; // TODO
                LastNewLine = true;
                BracketLevel = ParenLevel = BraceLevel = PendingDedents = IndentLevel = 0;
                IndentFormat = null;
                IncompleteString = null;
                MultiLineComment = false;
                if ((options & TokenizerOptions.Verbatim) != 0)
                {
                    CurWhiteSpace = new StringBuilder();
                    NextWhiteSpace = new StringBuilder();
                }
                else
                {
                    CurWhiteSpace = null;
                    NextWhiteSpace = null;
                }
                GroupingRecovery = null;
            }

            public override bool Equals(object obj)
            {
                if (obj is State)
                {
                    State other = (State)obj;
                    return other == this;
                }
                else
                {
                    return false;
                }
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public static bool operator ==(State left, State right)
            {
                if (left == null) return right == null;

                return left.BraceLevel == right.BraceLevel &&
                       left.BracketLevel == right.BracketLevel &&
                       left.IndentLevel == right.IndentLevel &&
                       left.ParenLevel == right.ParenLevel &&
                       left.PendingDedents == right.PendingDedents &&
                       left.LastNewLine == right.LastNewLine; /*&&
                   left.IncompleteString == right.IncompleteString;*/
            }

            public static bool operator !=(State left, State right)
            {
                return !(left == right);
            }

            #region IEquatable<State> Members

            public bool Equals(State other)
            {
                return this.Equals(other);
            }

            #endregion
        }

        /// <summary>
        /// Stores information to recover from a non-terminated grouping when we encounter a keyword which
        /// is only ever present outside of a grouping (e.g. class, def, etc...)
        /// 
        /// We only use this when the tokenizer has been created to use group recovery because this alters
        /// how we tokenize the language.  The parser creates the tokenizer in this mode.
        /// </summary>
        class GroupingRecovery
        {
            /// <summary>
            /// the new line kind that was in the grouping
            /// </summary>
            public readonly NewLineKind NewLineKind;
            /// <summary>
            /// the whitespace after the new line, for setting indent when we recover
            /// </summary>
            public readonly string NoAllocWhiteSpace;
            /// <summary>
            /// the # of spaces after the new line, for setting the indent when we recover
            /// </summary>
            public readonly int Spaces;
            /// <summary>
            /// the allocated whitespace after the new line, for setting the indent when we recover 
            /// </summary>
            public readonly StringBuilder Whitespace;
            /// <summary>
            /// the index within the file where the newline starts (not an index into the buffer)
            /// </summary>
            public readonly int NewlineStart;
            /// <summary>
            /// The amount of whitespace we had already collected before the newline, 
            /// so we can leave whitespace assocated w/ the newline attached to the newline
            /// </summary>
            public readonly int VerbatimWhiteSpaceLength;
            /// <summary>
            /// The starting position of the next token after the newline we hit, this GroupingRecovery is only 
            /// valid if this is unchanged which means we haven't ready an additional tokens.
            /// </summary>
            public readonly int TokenStart;

            public GroupingRecovery(NewLineKind newlineKind, string noAllocWhiteSpace, int spaces, StringBuilder whitespace, int newlineStart, int verbatimWhiteSpaceLength, int tokenStart)
            {
                NewLineKind = newlineKind;
                NoAllocWhiteSpace = noAllocWhiteSpace;
                Spaces = spaces;
                Whitespace = whitespace;
                NewlineStart = newlineStart;
                VerbatimWhiteSpaceLength = verbatimWhiteSpaceLength;
                TokenStart = tokenStart;
            }
        }

        /// <summary>
        /// Equality comparer that can compare strings to our current token w/o creating a new string first.
        /// </summary>
        class TokenEqualityComparer : IEqualityComparer<object>
        {
            private readonly Tokenizer _tokenizer;

            public TokenEqualityComparer(Tokenizer tokenizer)
            {
                _tokenizer = tokenizer;
            }

            #region IEqualityComparer<object> Members

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                if (x == _currentName)
                {
                    if (y == _currentName)
                    {
                        return true;
                    }

                    return Equals((string)y);
                }
                else if (y == _currentName)
                {
                    return Equals((string)x);
                }
                else
                {
                    return (string)x == (string)y;
                }
            }

            public int GetHashCode(object obj)
            {
                int result = 5381;
                if (obj == _currentName)
                {
                    char[] buffer = _tokenizer._buffer;
                    int start = _tokenizer._start, end = _tokenizer._tokenEnd;
                    for (int i = start; i < end; i++)
                    {
                        int c = buffer[i];
                        result = unchecked(((result << 5) + result) ^ c);
                    }
                }
                else
                {
                    string str = (string)obj;
                    for (int i = 0; i < str.Length; i++)
                    {
                        int c = str[i];
                        result = unchecked(((result << 5) + result) ^ c);
                    }
                }
                return result;
            }

            private bool Equals(string value)
            {
                int len = _tokenizer._tokenEnd - _tokenizer._start;
                if (len != value.Length)
                {
                    return false;
                }

                var buffer = _tokenizer._buffer;
                for (int i = 0, bufferIndex = _tokenizer._start; i < value.Length; i++, bufferIndex++)
                {
                    if (value[i] != buffer[bufferIndex])
                    {
                        return false;
                    }
                }

                return true;
            }

            #endregion
        }
    }

    enum NewLineKind
    {
        None,
        LineFeed,
        CarriageReturn,
        CarriageReturnLineFeed
    }

    static class NewLineKindExtensions
    {
        public static int GetSize(this NewLineKind kind)
        {
            switch (kind)
            {
                case NewLineKind.LineFeed: return 1;
                case NewLineKind.CarriageReturnLineFeed: return 2;
                case NewLineKind.CarriageReturn: return 2;
            }
            return 0;
        }

        public static string GetString(this NewLineKind kind)
        {
            switch (kind)
            {
                case NewLineKind.CarriageReturn: return "\r";
                case NewLineKind.CarriageReturnLineFeed: return "\r\n";
                case NewLineKind.LineFeed: return "\n";
            }
            throw new InvalidOperationException();
        }
    }
}
