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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VSGenero.Analysis.Parsing
{
    public enum TokenKind
    {
        EndOfFile = -1,
        Error = 0,
        NewLine = 1,
        Indent = 2,
        Dedent = 3,
        Comment = 4,
        Name = 8,
        Constant = 9,
        Dot = 10,
        QuestionMark = 11,

        // numeric expression operators
        FirstOperator = Add,
        LastOperator = SingleBar,
        Add = 20,
        Subtract = 21,
        Multiply = 22,
        Divide = 23,
        Assign = 24,
        Power = 26,

        // boolean expression operators
        DoubleEquals = 30,
        LessThan = 31,
        GreaterThan = 32,
        LessThanOrEqual = 33,
        GreaterThanOrEqual = 34,
        Equals = 35,
        NotEquals = 36,
        NotEqualsLTGT = 37,

        // string expression operators
        DoubleBar = 38,
        Exclamation = 39,
        SingleBar = 40,

        // other non-keyword tokens
        AtSymbol = 46,
        Ampersand = 47,
        LeftParenthesis = 48,
        RightParenthesis = 49,
        LeftBracket = 50,
        RightBracket = 51,
        LeftBrace = 52,
        RightBrace = 53,
        Comma = 54,
        Colon = 55,
        BackQuote = 56,
        Semicolon = 57,


        FirstLanguageKeyword = AbsoluteKeyword,
        LastLanguageKeyword = YesKeyword,
        AbsoluteKeyword = 75,
        AcceleratorKeyword,
        Accelerator2Keyword,
        Accelerator3Keyword,
        Accelerator4Keyword,
        AcceptKeyword,
        AccessoryTypeKeyword,
        ActionKeyword,
        AfterKeyword,
        AggregateKeyword,
        AggregateTextKeyword,
        AllKeyword,
        AllocateKeyword,
        AllRowsKeyword,
        AlterKeyword,
        AndKeyword,
        AnyKeyword,
        AppendKeyword,
        ApplicationKeyword,
        ArrayKeyword,
        AsciiKeyword,
        AscKeyword,
        AsKeyword,
        AtKeyword,
        AttributeKeyword,
        AttributesKeyword,
        AutoKeyword,
        AverageKeyword,
        AvgKeyword,
        BaseKeyword,
        BeforeKeyword,
        BeginKeyword,
        BetweenKeyword,
        BigintKeyword,
        BlackKeyword,
        BlinkKeyword,
        BlueKeyword,
        BoldKeyword,
        BooleanKeyword,
        BorderKeyword,
        BottomKeyword,
        BreakpointKeyword,
        BufferedKeyword,
        ButtonKeyword,
        ButtonTextHiddenKeyword,
        ByKeyword,
        ByteKeyword,
        CacheKeyword,
        CallKeyword,
        CancelKeyword,
        CaseKeyword,
        CastKeyword,
        CatchKeyword,
        CenturyKeyword,
        ChangeKeyword,
        CharacterKeyword,
        CharKeyword,
        CharLengthKeyword,
        CheckboxKeyword,
        CheckKeyword,
        CheckmarkKeyword,
        CircuitKeyword,
        ClearKeyword,
        ClippedKeyword,
        CloseKeyword,
        ClusterKeyword,
        CollapseKeyword,
        ColumnKeyword,
        ColumnsKeyword,
        CommandKeyword,
        CommentKeyword,
        CommentsKeyword,
        CommitKeyword,
        CommittedKeyword,
        CompactKeyword,
        ConstantKeyword,
        ConstrainedKeyword,
        ConstraintKeyword,
        ConstructKeyword,
        ContextMenuKeyword,
        ContinueKeyword,
        ControlKeyword,
        CopyKeyword,
        CountKeyword,
        CrcolsKeyword,
        CreateKeyword,
        CurrentKeyword,
        CursorKeyword,
        CyanKeyword,
        CycleKeyword,
        DatabaseKeyword,
        DateKeyword,
        DatetimeKeyword,
        DayKeyword,
        DeallocateKeyword,
        DecimalKeyword,
        DecKeyword,
        DeclareKeyword,
        DecodeKeyword,
        DefaultKeyword,
        DefaultsKeyword,
        DefaultViewKeyword,
        DeferKeyword,
        DefineKeyword,
        DeleteKeyword,
        DelimiterKeyword,
        DescKeyword,
        DescribeKeyword,
        DestinationKeyword,
        DetailActionKeyword,
        DetailButtonKeyword,
        DialogKeyword,
        DictionaryKeyword,
        DimensionKeyword,
        DimensionsKeyword,
        DimKeyword,
        DirtyKeyword,
        DisclosureIndicatorKeyword,
        DisplayKeyword,
        DistinctKeyword,
        DoKeyword,
        DoubleKeyword,
        DoubleClickKeyword,
        DownKeyword,
        Drag_EnterKeyword,
        Drag_FinishKeyword,
        Drag_FinishedKeyword,
        Drag_OverKeyword,
        Drag_StartKeyword,
        DropKeyword,
        DynamicKeyword,
        EditKeyword,
        ElseKeyword,
        EndifKeyword,
        EndKeyword,
        EnterKeyword,
        ErrorKeyword,
        EscKeyword,
        EscapeKeyword,
        EveryKeyword,
        ExclusiveKeyword,
        ExecKeyword,
        ExecuteKeyword,
        ExistsKeyword,
        ExitKeyword,
        ExpandKeyword,
        ExplainKeyword,
        ExtendKeyword,
        ExtentKeyword,
        ExternalKeyword,
        FalseKeyword,
        FetchKeyword,
        FglKeyword,
        Field_TouchedKeyword,
        FieldKeyword,
        FileKeyword,
        FinishKeyword,
        First_RowsKeyword,
        FirstKeyword,
        FloatKeyword,
        FlushKeyword,
        FolderKeyword,
        FontPitchKeyword,
        ForeachKeyword,
        ForeignKeyword,
        ForKeyword,
        FormatKeyword,
        FormKeyword,
        FormonlyKeyword,
        FoundKeyword,
        FractionKeyword,
        FreeKeyword,
        FromKeyword,
        FunctionKeyword,
        Get_FldbufKeyword,
        GlobalsKeyword,
        GoKeyword,
        GotoKeyword,
        GreenKeyword,
        GridKeyword,
        GroupKeyword,
        HandlerKeyword,
        HavingKeyword,
        HboxKeyword,
        HeaderKeyword,
        HeightKeyword,
        HelpKeyword,
        HiddenKeyword,
        HideKeyword,
        HoldKeyword,
        HourKeyword,
        IdleKeyword,
        IfdefKeyword,
        IfKeyword,
        IifKeyword,
        ImageKeyword,
        ImmediateKeyword,
        ImportKeyword,
        IncludeKeyword,
        IndexKeyword,
        IndicatorKeyword,
        InfieldKeyword,
        InitializeKeyword,
        InKeyword,
        IncrementKeyword,
        InnerKeyword,
        InOutKeyword,
        InputKeyword,
        InsertKeyword,
        InstanceOfKeyword,
        InstructionsKeyword,
        Int_FlagKeyword,
        IntegerKeyword,
        InterruptKeyword,
        IntervalKeyword,
        IntKeyword,
        IntoKeyword,
        InvisibleKeyword,
        IsKeyword,
        IsolationKeyword,
        ItemKeyword,
        JavaKeyword,
        JoinKeyword,
        KeepKeyword,
        KeyKeyword,
        LabelKeyword,
        LastKeyword,
        LayoutKeyword,
        LeftKeyword,
        LengthKeyword,
        LetKeyword,
        LikeKeyword,
        LimitKeyword,
        LineKeyword,
        LinenoKeyword,
        LinesKeyword,
        LoadKeyword,
        LocateKeyword,
        LockKeyword,
        LogKeyword,
        LongKeyword,
        LstrKeyword,
        MagentaKeyword,
        MainKeyword,
        MarginKeyword,
        MatchesKeyword,
        MaxKeyword,
        MaxCountKeyword,
        MaxvalueKeyword,
        MdyKeyword,
        MemoryKeyword,
        MenuKeyword,
        MessageKeyword,
        MiddleKeyword,
        MinHeightKeyword,
        MinKeyword,
        MinuteKeyword,
        MinvalueKeyword,
        MinWidthKeyword,
        ModeKeyword,
        ModKeyword,
        ModuleKeyword,
        MoneyKeyword,
        MonthKeyword,
        NameKeyword,
        NcharKeyword,
        NeedKeyword,
        NewKeyword,
        NextKeyword,
        NextpageKeyword,
        NoKeyword,
        NocacheKeyword,
        NocycleKeyword,
        NomaxvalueKeyword,
        NominvalueKeyword,
        NoorderKeyword,
        NormalKeyword,
        NotfoundKeyword,
        NotKeyword,
        NowKeyword,
        NullKeyword,
        NumericKeyword,
        NvarcharKeyword,
        NvlKeyword,
        OffKeyword,
        OfKeyword,
        OnKeyword,
        OpenKeyword,
        OptionKeyword,
        OptionsKeyword,
        OrderKeyword,
        OrdKeyword,
        OrKeyword,
        OtherwiseKeyword,
        OuterKeyword,
        OutKeyword,
        OutputKeyword,
        PageKeyword,
        PagenoKeyword,
        PauseKeyword,
        PercentKeyword,
        PictureKeyword,
        PipeKeyword,
        PrecisionKeyword,
        PrepareKeyword,
        PreviousKeyword,
        PrevpageKeyword,
        PrimaryKeyword,
        PrinterKeyword,
        PrintKeyword,
        PrintxKeyword,
        PriorKeyword,
        PrivateKeyword,
        ProgramKeyword,
        PromptKeyword,
        PublicKeyword,
        PutKeyword,
        Quit_FlagKeyword,
        QuitKeyword,
        RaiseKeyword,
        ReadKeyword,
        RealKeyword,
        RecordKeyword,
        RedKeyword,
        RelativeKeyword,
        RemoveKeyword,
        RenameKeyword,
        ReoptimizationKeyword,
        RepeatableKeyword,
        RepeatKeyword,
        ReportKeyword,
        RequiredKeyword,
        ResizeKeyword,
        RestartKeyword,
        ReturningKeyword,
        ReturnKeyword,
        ReturnsKeyword,
        ReverseKeyword,
        RightKeyword,
        RollbackKeyword,
        RowboundKeyword,
        RowKeyword,
        RowsKeyword,
        RunKeyword,
        SchemaKeyword,
        ScreenKeyword,
        ScrollGridKeyword,
        ScrollKeyword,
        SecondKeyword,
        SelectKeyword,
        SeparatorKeyword,
        SetKeyword,
        SequenceKeyword,
        SfmtKeyword,
        ShareKeyword,
        ShiftKeyword,
        ShowKeyword,
        ShortKeyword,
        SignalKeyword,
        SizeKeyword,
        SizepolicyKeyword,
        SkipKeyword,
        SleepKeyword,
        SmallfloatKeyword,
        SmallintKeyword,
        SpaceKeyword,
        SpacesKeyword,
        SpacingKeyword,
        SplitterKeyword,
        SqlErrMessageKeyword,
        SqlerrorKeyword,
        SqlKeyword,
        SqlStateKeyword,
        SqlwarningKeyword,
        StabilityKeyword,
        StackKeyword,
        StartKeyword,
        StatisticsKeyword,
        StatusKeyword,
        StepKeyword,
        StopKeyword,
        StringKeyword,
        StyleKeyword,
        SubdialogKeyword,
        SumKeyword,
        TabindexKeyword,
        TableKeyword,
        TablesKeyword,
        TagKeyword,
        TempKeyword,
        TerminateKeyword,
        TextKeyword,
        ThenKeyword,
        ThroughKeyword,
        ThruKeyword,
        TimeKeyword,
        TimerKeyword,
        TinyintKeyword,
        TitleKeyword,
        TodayKeyword,
        ToKeyword,
        ToolbarKeyword,
        TopKeyword,
        TopMenuKeyword,
        TrailerKeyword,
        TreeKeyword,
        TrueKeyword,
        TryKeyword,
        TypeKeyword,
        UnbufferedKeyword,
        UnconstrainedKeyword,
        UndefKeyword,
        UnderlineKeyword,
        UnhidableColumnsKeyword,
        UnionKeyword,
        UniqueKeyword,
        UnitsKeyword,
        UnloadKeyword,
        UnmovableColumnsKeyword,
        UnsizableColumnsKeyword,
        UnsortableColumnsKeyword,
        UpdateKeyword,
        UpKeyword,
        UserKeyword,
        UsingKeyword,
        ValidateKeyword,
        ValuecheckedKeyword,
        ValueKeyword,
        ValuesKeyword,
        ValueUncheckedKeyword,
        VarcharKeyword,
        VersionKeyword,
        VboxKeyword,
        ViewKeyword,
        WaitingKeyword,
        WaitKeyword,
        WantFixedPageSizeKeyword,
        WarningKeyword,
        WeekdayKeyword,
        WheneverKeyword,
        WhenKeyword,
        WhereKeyword,
        WhileKeyword,
        WhiteKeyword,
        WidthKeyword,
        WindowKeyword,
        WindowStyleKeyword,
        WithKeyword,
        WithoutKeyword,
        WordwrapKeyword,
        WorkKeyword,
        WrapKeyword,
        XmlKeyword,
        YearKeyword,
        YellowKeyword,
        YesKeyword,
        NLToken,
        ExplicitLineJoin
    }

    internal static class Tokens
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token EndOfFileToken = new VerbatimToken(TokenKind.EndOfFile, "", "<eof>");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token ImpliedNewLineToken = new VerbatimToken(TokenKind.NewLine, "", "<newline>");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NewLineToken = new VerbatimToken(TokenKind.NewLine, "\n", "<newline>");
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NewLineTokenCRLF = new VerbatimToken(TokenKind.NewLine, "\r\n", "<newline>");
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NewLineTokenCR = new VerbatimToken(TokenKind.NewLine, "\r", "<newline>");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NLToken = new VerbatimToken(TokenKind.NLToken, "\n", "<NL>");  // virtual token used for error reporting
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NLTokenCRLF = new VerbatimToken(TokenKind.NLToken, "\r\n", "<NL>");  // virtual token used for error reporting
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token NLTokenCR = new VerbatimToken(TokenKind.NLToken, "\r", "<NL>");  // virtual token used for error reporting

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token IndentToken = new DentToken(TokenKind.Indent, "<indent>");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Dedent")]
        public static readonly Token DedentToken = new DentToken(TokenKind.Dedent, "<dedent>");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly Token DotToken = new SymbolToken(TokenKind.Dot, ".");

        // Generated
        private static readonly Token symQuestionMark = new OperatorToken(TokenKind.QuestionMark, "?", 4);
        private static readonly Token symAddToken = new OperatorToken(TokenKind.Add, "+", 4);
        private static readonly Token symSubtractToken = new OperatorToken(TokenKind.Subtract, "-", 4);
        private static readonly Token symMultiplyToken = new OperatorToken(TokenKind.Multiply, "*", 5);
        private static readonly Token symDivideToken = new OperatorToken(TokenKind.Divide, "/", 5);
        private static readonly Token symPowerToken = new OperatorToken(TokenKind.Power, "**", 6);
        private static readonly Token symConcatToken = new OperatorToken(TokenKind.DoubleBar, "||", 6);
        private static readonly Token symSingleBarToken = new OperatorToken(TokenKind.SingleBar, "|", 6);
        private static readonly Token symNotEqualGTLT = new OperatorToken(TokenKind.NotEqualsLTGT, "<>", 6);
        private static readonly Token symNotEqual = new OperatorToken(TokenKind.NotEquals, "!=", 6);
        private static readonly Token symExclamation = new OperatorToken(TokenKind.Exclamation, "!", 6);
        private static readonly Token symLessThan = new OperatorToken(TokenKind.LessThan, "<", 6);
        private static readonly Token symLessThanEquals = new OperatorToken(TokenKind.LessThanOrEqual, "<=", 6);
        private static readonly Token symGreaterThan = new OperatorToken(TokenKind.GreaterThan, ">", 6);
        private static readonly Token symGreaterThanEquals = new OperatorToken(TokenKind.GreaterThanOrEqual, ">=", 6);
        private static readonly Token symLeftParenthesisToken = new SymbolToken(TokenKind.LeftParenthesis, "(");
        private static readonly Token symRightParenthesisToken = new SymbolToken(TokenKind.RightParenthesis, ")");
        private static readonly Token symLeftBracketToken = new SymbolToken(TokenKind.LeftBracket, "[");
        private static readonly Token symRightBracketToken = new SymbolToken(TokenKind.RightBracket, "]");
        private static readonly Token symLeftBraceToken = new SymbolToken(TokenKind.LeftBrace, "{");
        private static readonly Token symRightBraceToken = new SymbolToken(TokenKind.RightBrace, "}");
        private static readonly Token symCommaToken = new SymbolToken(TokenKind.Comma, ",");
        private static readonly Token symColonToken = new SymbolToken(TokenKind.Colon, ":");
        private static readonly Token symSemicolonToken = new SymbolToken(TokenKind.Semicolon, ";");
        private static readonly Token symDoubleEqualsToken = new OperatorToken(TokenKind.DoubleEquals, "==", -1);
        private static readonly Token symEqualsToken = new OperatorToken(TokenKind.Equals, "=", -1);
        private static readonly Token symAssign = new OperatorToken(TokenKind.Assign, ":=", -1);
        private static readonly Token symAmpersand = new OperatorToken(TokenKind.Ampersand, "&", -1);
        private static readonly Token symAtSymbol = new SymbolToken(TokenKind.AtSymbol, "@");

        public static Token GetSymbolToken(string tokenText)
        {
            foreach (var field in typeof(Tokens).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                                               .Where(x => x.FieldType == typeof(Token)))
            {
                var val = field.GetValue(null);
                if (val is OperatorToken &&
                   (val as OperatorToken).Image == tokenText)
                {
                    return (Token)val;
                }
                else if(val is SymbolToken &&
                        (val as SymbolToken).Image == tokenText)
                {
                    return (Token)val;
                }
            }
            return null;
        }

        private static Dictionary<string, TokenKind> _keywords;
        public static Dictionary<string, TokenKind> Keywords
        {
            get
            {
                if (_keywords == null)
                {
                    InitializeTokens();
                }
                return _keywords;
            }
        }

        private static Dictionary<TokenKind, string> _tokenKinds;
        public static Dictionary<TokenKind, string> TokenKinds
        {
            get
            {
                if (_tokenKinds == null)
                {
                    InitializeTokens();
                }
                return _tokenKinds;
            }
        }

        private static object _tokLock = new object();

        private static void InitializeTokens()
        {
            lock (_tokLock)
            {
                if (_keywords == null)
                {
                    var keywords = Enum.GetNames(typeof(TokenKind));
                    var values = Enum.GetValues(typeof(TokenKind)).OfType<TokenKind>().ToArray();
                    _keywords = new Dictionary<string, TokenKind>(keywords.Length, StringComparer.OrdinalIgnoreCase);
                    _tokenKinds = new Dictionary<TokenKind, string>(keywords.Length);
                    for (int i = 0; i < keywords.Length; i++)
                    {
                        if (keywords[i].EndsWith("Keyword") && !keywords[i].Contains("Language"))
                        {
                            string key = keywords[i].Replace("Keyword", string.Empty);
                            TokenKind val = (TokenKind)values[i];
                            if (!_keywords.ContainsKey(key))
                            {
                                _keywords.Add(key.ToLower(), val);
                                _tokenKinds.Add(val, key.ToLower());
                            }
                            else
                            {
                                // This should never happen, since we're now handling the race condition
                                int j = 0;
                            }
                        }
                    }
                }
            }
        }

        public static Token GetToken(string possibleKeyword)
        {
            Token tok = null;
            InitializeTokens();
            TokenKind tryKind;
            if (_keywords.TryGetValue(possibleKeyword, out tryKind))
            {
                tok = new SymbolToken(tryKind, possibleKeyword);
            }
            return tok;
        }

        public static Token AmpersandToken
        {
            get { return symAmpersand; }
        }

        public static Token AtSymbol
        {
            get { return symAtSymbol; }
        }

        public static Token PowerToken
        {
            get { return symPowerToken; }
        }

        public static Token ConcatToken
        {
            get { return symConcatToken; }
        }

        public static Token SingleBarToken
        {
            get { return symSingleBarToken; }
        }

        public static Token NotEqualsToken
        {
            get { return symNotEqual; }
        }

        public static Token ExclamationToken
        {
            get { return symExclamation; }
        }

        public static Token NotEqualsLTGTToken
        {
            get { return symNotEqualGTLT; }
        }

        public static Token LessThanToken
        {
            get { return symLessThan; }
        }

        public static Token LessThanEqualToken
        {
            get { return symLessThanEquals; }
        }

        public static Token GreaterThanToken
        {
            get { return symGreaterThan; }
        }

        public static Token GreaterThanEqualToken
        {
            get { return symGreaterThanEquals; }
        }

        public static Token LeftParenthesisToken
        {
            get { return symLeftParenthesisToken; }
        }

        public static Token RightParenthesisToken
        {
            get { return symRightParenthesisToken; }
        }

        public static Token LeftBracketToken
        {
            get { return symLeftBracketToken; }
        }

        public static Token RightBracketToken
        {
            get { return symRightBracketToken; }
        }

        public static Token LeftBraceToken
        {
            get { return symLeftBraceToken; }
        }

        public static Token RightBraceToken
        {
            get { return symRightBraceToken; }
        }

        public static Token CommaToken
        {
            get { return symCommaToken; }
        }

        public static Token ColonToken
        {
            get { return symColonToken; }
        }

        public static Token SemicolonToken
        {
            get { return symSemicolonToken; }
        }

        public static Token EqualsToken
        {
            get { return symEqualsToken; }
        }

        public static Token DoubleEqualsToken
        {
            get { return symDoubleEqualsToken; }
        }

        public static Token AssignToken
        {
            get { return symAssign; }
        }

        public static Token AddToken
        {
            get { return symAddToken; }
        }

        public static Token QuestionMarkToken
        {
            get { return symQuestionMark; }
        }

        public static Token SubtractToken
        {
            get { return symSubtractToken; }
        }

        public static Token MultiplyToken
        {
            get { return symMultiplyToken; }
        }

        public static Token DivideToken
        {
            get { return symDivideToken; }
        }
    }
}
