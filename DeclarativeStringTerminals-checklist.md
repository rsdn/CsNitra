# DeclarativeStringTerminals — checklist

План: заменить hand-written терминалы `CSharpTerminals` декларативными `[Regex]`-терминалами
и правилами грамматик (в стиле `VerbatimInterpolatedStringLiteral` / `RawInterpolatedStringLiteral`).
Парсер (`ExtensibleParser`) и язык текстовых грамматик не меняем.

Состав:
- 9 терминалов конвертируются (P1–P4): StringLiteral, VerbatimStringLiteral, RawStringLiteral,
  InterpolatedRegularText, InterpolatedRegularEscape, InterpolatedVerbatimText,
  RegularFormatText, VerbatimFormatText, RawFormatText.
- 3 контекстных терминала остаются hand-written (Deviation D1): RawOpenBraceLiteral,
  RawCloseBraceLiteral, RawHoleOpenBraces.

- [✅] 1. Regex-текстовые раны: InterpolatedRegularText `[^\{\}\\"]+`, InterpolatedVerbatimText
      `[^\{\}"]+`, RawFormatText `[^}]*`; удалить императивные record/поле/фабрики и
      умерший `TryScanNonBraceQuoteRun` (CSharpTerminals.cs). Грамматики не трогаем.
- [~] 2. StringLiteral + VerbatimStringLiteral → правила Cs1.grammar (в стиле
      `VerbatimInterpolatedStringLiteral`); новые терминалы StringEscape, StringText, NonQuoteText;
      удалить императивные терминалы строк.
  - [✅] 2.1 Реализация: правила + [Regex]-терминалы (raw-строки), сборка CSharpGrammar — 0 ошибок,
        паттерны проверены в generated-коде.
  - [ ] 2.2 Тесты StringLiteral: переписать юнит-тесты терминала на парс-тесты правила
        `StringLiteral` (start rule), по имени правила, без анализа грамматики.
  - [ ] 2.3 Тесты VerbatimStringLiteral: то же для правила `VerbatimStringLiteral`.
- [✅] 3. RawStringLiteral → Cs11.grammar: итог — одно [Regex]-целое `RawString`
      (open-ран 3+, контент = кавычные части 1..2 + [^"], контент ≥ 1, close-ран 3+) +
      обёртка-правило `RawStringLiteral = RawString;` + альтернатива в Primary;
      EmbeddedResource в csproj + EmbeddedGrammar.LoadCs11Grammar; императивный терминал и
      промежуточные RawQuoteRun/RawQuoteContent удалены. `context("\""+, ...)` отброшен:
      post-terminal trivia-skip склеивает кавычные раны источника (см. progress3).
- [✅] 4. RegularFormatText + VerbatimFormatText → [Regex]-терминалы (одно целое до '}',
      не part-правила: внутри матча нет trivia-skip, accept/reject на уровне правила = как у
      императивных); InterpolatedRegularEscape → [Regex] (набор = StringEscape, D7);
      StringLiteralScanner.cs удалён (все методы мертвы).
- [ ] 5. Финальный гейт: сборка решения, полный прогон тестов, ревизия diff, отчёт.

## Deviations

- D1: RawOpenBraceLiteral / RawCloseBraceLiteral / RawHoleOpenBraces остаются hand-written:
  матчинг зависит от `Parser.ContextCount` (D) и выражает ДИАПАЗОНЫ запусков скобок
  (1..D-1, D-1..2D-2), что невыразимо в текущем языке грамматик: повторение — только точный
  счётчик `{N}` или контекстный `{n}` (Parser.cs:679 `repeat.Count ?? ContextCount`),
  диапазона нет. Территория Этапа 5 (повторение с контекстной границей).
- D2: Newline-прозрачность для StringLiteral/VerbatimStringLiteral/RawStringLiteral: newline между
  элементами правила съедается глобальным trivia-сканером (для RawString — [^"] внутри одного
  regex-матча) → грамматика принимает ввод, который Roslyn отклоняет (raw-newline в
  single-line plain/raw, пустое multi-line тело raw, CS9002). Та же категория, что
  задокументированные расхождения для интерполяции (docs/InterpolatedStringGrammar.md §6.3/§7.5);
  соответствующие юнит-кейсы «reject» переводятся в тесты-документации «accept».
- D3: `""""` (4 кавычки) в expression-контексте принимается как два пустых plain-литерала
  (более permisсивно, чем Roslyn raw-N=4 scan). Аналог зафиксирован в §2.5 дизайна
  (6 кавычек у interpolated raw). На уровне start-rule фрагмента кейсы unit-тестов сохраняют reject.
  То же для 9 кавычек `"""""""""`: принимается как open 3 + content 3 + close 3 (сканер: -1,
  пустое тело) — reject→accept, только невалидный C#.
- D7: InterpolatedRegularEscape / RegularFormatText — [Regex] матчит ФОРМУ escape, а не
  hex-ЗНАЧЕНИЕ: escape'ы, разрешающиеся в '{' / '}' (\x7B..\x7D, \u007B..\u007D,
  \U0000007B..\U0000007D), принимаются (Roslyn: CS1053 reject). У движка regex нет арифметики
  hex-значений (категория D4); reject→accept, только невалидный C#. Кейс \u007B переведён в
  тест-документацию accept (InterpolatedStringTests).
- D4: `\x`-escape: regex-ран шестнадцатеричных цифр неограничен (движок regex без bounded
  repetition), у сканера — до 4 цифр. Accept/reject идентичны, смещается только граница escape.
- D5: StringText исключает только `"`, `\`, LF, CR — НЕ U+0085/U+2028/U+2029 (невыразимо в
  паттерне: движок regex без `\u`-экранов, сырые символы ломают generated-код как line terminators).
  Plain-строка с сырым U+0085/U+2028/U+2029 принимается (сканер отклонял) — категория D2.
- D6: `[Regex]`-паттерны пишутся raw-строками `"""..."""` (конвенция AGENTS.md + исключает
  экрани-ошибки C#-строк; первый subagent P2 уронил StringText через non-verbatim экрани).
