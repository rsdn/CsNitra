using System.Collections.Generic;
using ExtensibleParser;

namespace CSharpGrammar;

[TerminalMatcher]
public sealed partial class CSharpTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"[0-9]+")]
    public static partial Terminal DecimalIntegerLiteral();

    [Regex(@"0[xX][0-9a-fA-F]+")]
    public static partial Terminal HexIntegerLiteral();

    [Regex(@"0[0-7]+")]
    public static partial Terminal OctalIntegerLiteral();

    [Regex(@"[uU]?[lL]?|[lL][uU]?")]
    public static partial Terminal IntegerSuffix();

    [Regex(@"[0-9]+\.[0-9]*|\.[0-9]+")]
    public static partial Terminal DecimalRealLiteral();

    [Regex(@"[eE][+-]?[0-9]+")]
    public static partial Terminal Exponent();

    [Regex(@"[fFdDmM]")]
    public static partial Terminal RealSuffix();

    public static Terminal StringLiteral() => _stringLiteral;

    public static Terminal InterpolatedStringLiteral() => _interpolatedStringLiteral;

    public static Terminal VerbatimStringLiteral() => _verbatimStringLiteral;

    public static Terminal RawStringLiteral() => _rawStringLiteral;

    public static Terminal RawInterpolatedStringLiteral() => _rawInterpolatedStringLiteral;

    [Regex(@"'([^'\n\\]|\\.)'")]
    public static partial Terminal CharLiteral();

    public static Terminal KwAbstract() => Kw("KwAbstract", "abstract");

    public static Terminal KwAlias() => Kw("KwAlias", "alias");

    public static Terminal KwArglist() => Kw("KwArglist", "__arglist");

    public static Terminal KwAs() => Kw("KwAs", "as");

    public static Terminal KwBase() => Kw("KwBase", "base");

    public static Terminal KwBool() => Kw("KwBool", "bool");

    public static Terminal KwBreak() => Kw("KwBreak", "break");

    public static Terminal KwByte() => Kw("KwByte", "byte");

    public static Terminal KwCase() => Kw("KwCase", "case");

    public static Terminal KwCatch() => Kw("KwCatch", "catch");

    public static Terminal KwChar() => Kw("KwChar", "char");

    public static Terminal KwChecked() => Kw("KwChecked", "checked");

    public static Terminal KwClass() => Kw("KwClass", "class");

    public static Terminal KwConst() => Kw("KwConst", "const");

    public static Terminal KwContinue() => Kw("KwContinue", "continue");

    public static Terminal KwDecimal() => Kw("KwDecimal", "decimal");

    public static Terminal KwDefault() => Kw("KwDefault", "default");

    public static Terminal KwDelegate() => Kw("KwDelegate", "delegate");

    public static Terminal KwDo() => Kw("KwDo", "do");

    public static Terminal KwDouble() => Kw("KwDouble", "double");

    public static Terminal KwElse() => Kw("KwElse", "else");

    public static Terminal KwEnum() => Kw("KwEnum", "enum");

    public static Terminal KwEvent() => Kw("KwEvent", "event");

    public static Terminal KwExplicit() => Kw("KwExplicit", "explicit");

    public static Terminal KwExtern() => Kw("KwExtern", "extern");

    public static Terminal KwFalse() => Kw("KwFalse", "false");

    public static Terminal KwFinally() => Kw("KwFinally", "finally");

    public static Terminal KwFixed() => Kw("KwFixed", "fixed");

    public static Terminal KwFloat() => Kw("KwFloat", "float");

    public static Terminal KwFor() => Kw("KwFor", "for");

    public static Terminal KwForeach() => Kw("KwForeach", "foreach");

    public static Terminal KwGoto() => Kw("KwGoto", "goto");

    public static Terminal KwIf() => Kw("KwIf", "if");

    public static Terminal KwImplicit() => Kw("KwImplicit", "implicit");

    public static Terminal KwIn() => Kw("KwIn", "in");

    public static Terminal KwInt() => Kw("KwInt", "int");

    public static Terminal KwInterface() => Kw("KwInterface", "interface");

    public static Terminal KwInternal() => Kw("KwInternal", "internal");

    public static Terminal KwIs() => Kw("KwIs", "is");

    public static Terminal KwLock() => Kw("KwLock", "lock");

    public static Terminal KwLong() => Kw("KwLong", "long");

    public static Terminal KwMakeref() => Kw("KwMakeref", "__makeref");

    public static Terminal KwNamespace() => Kw("KwNamespace", "namespace");

    public static Terminal KwNew() => Kw("KwNew", "new");

    public static Terminal KwNull() => Kw("KwNull", "null");

    public static Terminal KwObject() => Kw("KwObject", "object");

    public static Terminal KwOperator() => Kw("KwOperator", "operator");

    public static Terminal KwOut() => Kw("KwOut", "out");

    public static Terminal KwOverride() => Kw("KwOverride", "override");

    public static Terminal KwParams() => Kw("KwParams", "params");

    public static Terminal KwPrivate() => Kw("KwPrivate", "private");

    public static Terminal KwProtected() => Kw("KwProtected", "protected");

    public static Terminal KwPublic() => Kw("KwPublic", "public");

    public static Terminal KwReadonly() => Kw("KwReadonly", "readonly");

    public static Terminal KwRef() => Kw("KwRef", "ref");

    public static Terminal KwReftype() => Kw("KwReftype", "__reftype");

    public static Terminal KwRefvalue() => Kw("KwRefvalue", "__refvalue");

    public static Terminal KwReturn() => Kw("KwReturn", "return");

    public static Terminal KwSbyte() => Kw("KwSbyte", "sbyte");

    public static Terminal KwSealed() => Kw("KwSealed", "sealed");

    public static Terminal KwShort() => Kw("KwShort", "short");

    public static Terminal KwSizeof() => Kw("KwSizeof", "sizeof");

    public static Terminal KwStackalloc() => Kw("KwStackalloc", "stackalloc");

    public static Terminal KwStatic() => Kw("KwStatic", "static");

    public static Terminal KwString() => Kw("KwString", "string");

    public static Terminal KwStruct() => Kw("KwStruct", "struct");

    public static Terminal KwSwitch() => Kw("KwSwitch", "switch");

    public static Terminal KwThis() => Kw("KwThis", "this");

    public static Terminal KwThrow() => Kw("KwThrow", "throw");

    public static Terminal KwTrue() => Kw("KwTrue", "true");

    public static Terminal KwTry() => Kw("KwTry", "try");

    public static Terminal KwTypeof() => Kw("KwTypeof", "typeof");

    public static Terminal KwUInt() => Kw("KwUInt", "uint");

    public static Terminal KwUlong() => Kw("KwUlong", "ulong");

    public static Terminal KwUnchecked() => Kw("KwUnchecked", "unchecked");

    public static Terminal KwUnsafe() => Kw("KwUnsafe", "unsafe");

    public static Terminal KwUshort() => Kw("KwUshort", "ushort");

    public static Terminal KwUsing() => Kw("KwUsing", "using");

    public static Terminal KwVirtual() => Kw("KwVirtual", "virtual");

    public static Terminal KwVoid() => Kw("KwVoid", "void");

    public static Terminal KwVolatile() => Kw("KwVolatile", "volatile");

    public static Terminal KwWhile() => Kw("KwWhile", "while");

    private static readonly object _keywordLock = new();

    private static readonly Dictionary<string, KeywordTerminal> _keywords = new();

    private static KeywordTerminal Kw(string kind, string word)
    {
        lock (_keywordLock)
        {
            if (!_keywords.TryGetValue(kind, out var terminal))
            {
                _keywords[kind] = terminal = new KeywordTerminal(kind, word);
            }
            return terminal;
        }
    }

    public static Terminal Trivia() => _trivia;

    public static IReadOnlyList<Terminal> GetAll() => [
        Trivia(),
        Identifier(),
        DecimalIntegerLiteral(),
        HexIntegerLiteral(),
        OctalIntegerLiteral(),
        IntegerSuffix(),
        DecimalRealLiteral(),
        Exponent(),
        RealSuffix(),
        StringLiteral(),
        InterpolatedStringLiteral(),
        VerbatimStringLiteral(),
        RawStringLiteral(),
        RawInterpolatedStringLiteral(),
        CharLiteral(),
        KwAbstract(),
        KwAlias(),
        KwArglist(),
        KwAs(),
        KwBase(),
        KwBool(),
        KwBreak(),
        KwByte(),
        KwCase(),
        KwCatch(),
        KwChar(),
        KwChecked(),
        KwClass(),
        KwConst(),
        KwContinue(),
        KwDecimal(),
        KwDefault(),
        KwDelegate(),
        KwDo(),
        KwDouble(),
        KwElse(),
        KwEnum(),
        KwEvent(),
        KwExplicit(),
        KwExtern(),
        KwFalse(),
        KwFinally(),
        KwFixed(),
        KwFloat(),
        KwFor(),
        KwForeach(),
        KwGoto(),
        KwIf(),
        KwImplicit(),
        KwIn(),
        KwInt(),
        KwInterface(),
        KwInternal(),
        KwIs(),
        KwLock(),
        KwLong(),
        KwMakeref(),
        KwNamespace(),
        KwNew(),
        KwNull(),
        KwObject(),
        KwOperator(),
        KwOut(),
        KwOverride(),
        KwParams(),
        KwPrivate(),
        KwProtected(),
        KwPublic(),
        KwReadonly(),
        KwRef(),
        KwReftype(),
        KwRefvalue(),
        KwReturn(),
        KwSbyte(),
        KwSealed(),
        KwShort(),
        KwSizeof(),
        KwStackalloc(),
        KwStatic(),
        KwString(),
        KwStruct(),
        KwSwitch(),
        KwThis(),
        KwThrow(),
        KwTrue(),
        KwTry(),
        KwTypeof(),
        KwUInt(),
        KwUlong(),
        KwUnchecked(),
        KwUnsafe(),
        KwUshort(),
        KwUsing(),
        KwVirtual(),
        KwVoid(),
        KwVolatile(),
        KwWhile()
    ];

    private static readonly Terminal _trivia = new TriviaTerminal();

    private static readonly Terminal _stringLiteral = new StringLiteralTerminal();

    private static readonly Terminal _interpolatedStringLiteral = new InterpolatedStringLiteralTerminal();

    private static readonly Terminal _verbatimStringLiteral = new VerbatimStringLiteralTerminal();

    private static readonly Terminal _rawStringLiteral = new RawStringLiteralTerminal();

    private static readonly Terminal _rawInterpolatedStringLiteral = new RawInterpolatedStringLiteralTerminal();

    private sealed record StringLiteralTerminal : Terminal
    {
        public StringLiteralTerminal() : base("StringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanPlainString(input, startPos);

        public override string ToString() => "StringLiteral";
    }

    private sealed record InterpolatedStringLiteralTerminal : Terminal
    {
        public InterpolatedStringLiteralTerminal() : base("InterpolatedStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = StringLiteralScanner.TryScanInterpolatedString(input, startPos);
            if (length >= 0)
                return length;

            return StringLiteralScanner.TryScanAtInterpolatedString(input, startPos);
        }

        public override string ToString() => "InterpolatedStringLiteral";
    }

    private sealed record VerbatimStringLiteralTerminal : Terminal
    {
        public VerbatimStringLiteralTerminal() : base("VerbatimStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanVerbatimString(input, startPos);

        public override string ToString() => "VerbatimStringLiteral";
    }

    private sealed record RawStringLiteralTerminal : Terminal
    {
        public RawStringLiteralTerminal() : base("RawStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanRawString(input, startPos);

        public override string ToString() => "RawStringLiteral";
    }

    private sealed record RawInterpolatedStringLiteralTerminal : Terminal
    {
        public RawInterpolatedStringLiteralTerminal() : base("RawInterpolatedStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanRawInterpolatedString(input, startPos);

        public override string ToString() => "RawInterpolatedStringLiteral";
    }

    private sealed record TriviaTerminal : Terminal
    {
        public TriviaTerminal() : base("Trivia")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var pos = startPos;
            var length = input.Length;

            while (pos < length)
            {
                var c = input[pos];

                if (char.IsWhiteSpace(c))
                {
                    while (pos < length && char.IsWhiteSpace(input[pos]))
                        pos++;
                    continue;
                }

                if (c != '/' || pos + 1 >= length)
                    break;

                var next = input[pos + 1];

                if (next == '/')
                {
                    while (pos < length && input[pos] is not '\n' and not '\r')
                        pos++;
                    continue;
                }

                if (next == '*')
                {
                    pos += 2;
                    var depth = 1;
                    while (pos < length && depth > 0)
                    {
                        if (pos + 1 < length && input[pos] == '*' && input[pos + 1] == '/')
                        {
                            depth--;
                            pos += 2;
                        }
                        else if (pos + 1 < length && input[pos] == '/' && input[pos + 1] == '*')
                        {
                            depth++;
                            pos += 2;
                        }
                        else
                        {
                            pos++;
                        }
                    }
                    continue;
                }

                break;
            }

            return pos - startPos;
        }

        public override string ToString() => "Trivia";
    }

    private sealed record KeywordTerminal : Terminal
    {
        public KeywordTerminal(string kind, string word) : base(kind)
        {
            Word = word;
        }

        public string Word { get; }

        public override int TryMatch(string input, int startPos)
        {
            var wordLength = Word.Length;
            if (startPos + wordLength > input.Length)
                return -1;

            for (var i = 0; i < wordLength; i++)
            {
                if (input[startPos + i] != Word[i])
                    return -1;
            }

            var endPos = startPos + wordLength;
            return endPos < input.Length && IsIdentifierPart(input[endPos]) ? -1 : wordLength;
        }

        private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

        public override string ToString() => Word;
    }
}
