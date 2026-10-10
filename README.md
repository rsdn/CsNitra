[![.NET](https://github.com/rsdn/CsNitra/actions/workflows/dotnet.yml/badge.svg)](https://github.com/rsdn/CsNitra/actions/workflows/dotnet.yml)

# CsNitra: The Extensible Language Framework

**Build programming languages that grow with your needs.** CsNitra is a next-generation framework for creating truly extensible programming languages, DSLs, and tools. From simple configuration parsers to full-featured programming languages with IDE support - all with unprecedented extensibility.

## 🎯 What is CsNitra?

CsNitra is not just another parser. It's a **complete framework for language creation** that enables languages to extend their own syntax dynamically, even during parsing. Imagine a programming language where you can add new operators, keywords, and syntax constructs without modifying the compiler itself.

## ✨ The World's Most Advanced Parser Engine

CsNitra's parser is, without exaggeration, the most advanced parsing engine available today. It combines multiple state-of-the-art parsing techniques into a single, cohesive system:

### **PEG (Parsing Expression Grammar)**
- **Lexerless Design**: Each grammar rule defines its own token boundaries, eliminating the traditional separation between lexer and parser. This allows parsing languages with context-sensitive keywords and mixed token sets without the conflicts typical of lexer-based approaches. For example, in CppSimplifiedParser, the `AnyLine` terminal skips lines that don't contain namespace or enum declarations, while the `NamespaceOrEnumStartOrBrace` rule lets the parser selectively parse only the structures of interest, ignoring irrelevant code. This enables parsing of complex languages like C++ without preprocessing or a separate lexing phase.
- **Predicates**: Lookahead (`&`) and negative lookahead (`!`) without consuming input
- **Memoization**: Packrat parsing guarantees linear-time performance for any grammar
- **Longest Match Selection**: Tries all alternatives and selects the one that parses the longest substring, eliminating ordering bias
- **Deterministic Longest-Match**: The parser selects the alternative that parses the longest substring, avoiding ambiguities in most cases. In the rare event of an ambiguity (e.g., two alternatives parsing substrings of equal length), the parser will report an error, allowing you to resolve it using lookahead predicates (`&`, `!`).


### **TDOPP (Top-Down Operator Precedence Parsing)**
- **Natural Operator Syntax**: Write expression grammars in intuitive left-recursive form
- **Dynamic Precedence**: Operators with precedence levels and associativity
- **No Grammar Clutter**: No need for complex nested rule hierarchies

### **Generalized Parsing**
- **Longest Match Selection**: Tries all alternatives and selects the most successful match
- **Intelligent Backtracking**: Smart memoization prevents exponential backtracking
- **Context-Aware Choices**: Each parsing decision considers full context

### **Automatic syntax trees Construction**
- **Zero Boilerplate**: Syntax trees are automatically constructed during parsing
- **Rich Node Types**: Sequences, lists, optionals, terminals with full metadata
- **Complete Position Tracking**: Every node knows its exact source location
- **Easy Visitor Pattern**: Built-in support for syntax trees traversal and transformation

### **First-Class Separated List Support**
- **Natural Grammar Syntax**: Write `(Element; Separator)*` instead of manual recursion
- **Automatic syntax trees Construction**: Lists generate clean syntax trees nodes with elements and separators
- **Flexible Trailing Separators**: Choose between optional, required, or forbidden trailing separators
- **Empty List Support**: Handle empty lists naturally in your grammar

### **High-Performance Execution**
- **Linear-Time Guarantee**: Thanks to packrat memoization
- **Minimal Allocation**: Optimized data structures reduce GC pressure
- **Terminal Optimization**: Regex patterns compiled to efficient DFA matchers
- **Thread-Safe Design**: Parser instances can be reused across threads

### **Regex Terminal Generator**
- **Compile-Time DFA Generation**: Regular expressions in C# attributes are converted to deterministic finite automata
- **Efficient Matching**: Guaranteed O(n) matching time with input length
- **No Runtime Regex Compilation**: All patterns are pre-compiled for maximum performance

### **Error Recovery & Diagnostics**
See the [grammar author guide](docs/RecoveryAuthorGuide.md) and
[recovery implementation and validation report](docs/RecoveryCompletionReport-2026-10-10.md).
Recovered EOF results can contain errors; semantic consumers must check diagnostics or use compiler mode.

- **Intelligent Error Recovery**: Automatically skips to recoverable points
- **Rich Error Messages**: Context-aware error highlighting with expected tokens
- **Fault-Tolerant Parsing**: Continue parsing after errors for IDE scenarios
- **Rule Stack Traces**: Detailed tracing of parsing decisions for debugging

## 🚀 Complete Feature Showcase

### **Dynamic Syntax Extensibility**
Languages can extend themselves **on-the-fly** during parsing:

```csharp
// Base language - simple arithmetic
Expr = 
  | Number
  | Ident
  | Expr "+" Expr : 100
  | Expr "*" Expr : 200

// In your source file, extend the language:
using MathExtensions.syntax;   // Load calculus syntax
using LinearAlgebra.syntax;    // Load matrix operations

// Now use the extended syntax:
var matrix = [1, 2, 3; 4, 5, 6; 7, 8, 9];
var result = A * B + C;
var integral = ∫(x² + 2x + 1) dx;  // Calculus syntax
```

## 🗺️ The Roadmap: From Parser to Complete Language Framework

While the current focus is on building the world's most advanced parser, CsNitra is designed from the ground up to evolve into a complete, extensible language framework. Here's our roadmap:

### **Phase 1: Advanced Parsing Engine** (Current Focus ✅)
- ✅ PEG with predicates and memoization
- ✅ TDOPP operator precedence parsing  
- ✅ Generalized longest-match selection
- ✅ Automatic AST generation with first-class list support
- ✅ Linear-time performance guarantee
- ✅ Regex-to-DFA terminal generator
- ✅ Intelligent error recovery
- ✅ Dynamic syntax extensibility
- ✅ Lexerless, context-aware tokenization
- 🔄 Automatic error recovery (under development)

### **Phase 2: Type System and Compilation Pipeline** (In Progress 🔄)
- 🔄 **Type System Foundation**: Rich type system with type inference and gradual typing
- 🔄 **Compilation Pipeline**: Multi-stage compilation with pluggable optimizations
- 🔄 **Quasi-Quotation & Macros**: Nemerle-style macros with compile-time code transformation
- 🔄 **Language Extensions**: Runtime and compile-time language extensibility

### **Phase 3: Full Language Ecosystem** (Planned 📅)
- 📅 **Language Protocol Server (LPS)**: Full IDE integration for all language extensions
- 📅 **Multi-Platform Code Generation**: Target .NET, JVM, WebAssembly, and native platforms
- 📅 **Automatic Metadata Generation**: Platform-specific metadata for .NET assemblies, JVM bytecode, etc.
- 📅 **Package Management**: Repository for language extensions and syntax packages
- 📅 **Cross-Language Interop**: Seamless interoperability between extended languages

## 🎯 The Ultimate Goal

CsNitra aims to become the foundational framework for creating **truly extensible programming languages** - languages that can evolve and adapt to new domains without losing compatibility or tooling support. Imagine creating a language that starts as a simple scripting tool but can grow into a full-stack development platform, all while maintaining perfect backward compatibility and IDE support.
