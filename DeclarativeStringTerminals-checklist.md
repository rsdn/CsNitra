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
- [ ] 3. RawStringLiteral → новый Cs11.grammar: `context`-правило (N-кавычки, N≥1 через
      `context("\""+, ...)` + закрывающий `"{n}" ")*`), RawLiteralPart, альтернатива в Primary;
      EmbeddedResource в csproj + EmbeddedGrammar.LoadCs11Grammar; удалить императивный терминал.
- [ ] 4. RegularFormatText + VerbatimFormatText → правила Cs6.grammar (part-правила);
      новые терминалы RegularFormatChar, VerbatimFormatChar; удалить императивные format-терминалы
      и умерший StringLiteralScanner.cs.
- [ ] 5. Финальный гейт: сборка решения, полный прогон тестов, ревизия diff, отчёт.

## Deviations

- D1: RawOpenBraceLiteral / RawCloseBraceLiteral / RawHoleOpenBraces остаются hand-written:
  матчинг зависит от `Parser.ContextCount` (D) и выражает ДИАПАЗОНЫ запусков скобок
  (1..D-1, D-1..2D-2), что невыразимо в текущем языке грамматик (нет повторения с
  контекстной границей; Repeat — только точный счётчик). Территория Этапа 5 (параметризуемые правила).
- D2: Newline-прозрачность для StringLiteral/VerbatimStringLiteral/RawStringLiteral: newline между
  элементами правила съедается глобальным trivia-сканером → грамматика принимает ввод, который
  Roslyn отклоняет (raw-newline в single-line plain/raw). Та же категория, что
  задокументированные расхождения для интерполяции (docs/InterpolatedStringGrammar.md §6.3/§7.5);
  соответствующие юнит-кейсы «reject» переводятся в тесты-документации «accept».
- D3: `""""` (4 кавычки) в expression-контексте принимается как два пустых plain-литерала
  (более permisсивно, чем Roslyn raw-N=4 scan). Аналог зафиксирован в §2.5 дизайна
  (6 кавычек у interpolated raw). На уровне start-rule фрагмента кейсы unit-тестов сохраняют reject.
- D4: `\x`-escape: regex-ран шестнадцатеричных цифр неограничен (движок regex без bounded
  repetition), у сканера — до 4 цифр. Accept/reject идентичны, смещается только граница escape.
- D5: StringText исключает только `"`, `\`, LF, CR — НЕ U+0085/U+2028/U+2029 (невыразимо в
  паттерне: движок regex без `\u`-экранов, сырые символы ломают generated-код как line terminators).
  Plain-строка с сырым U+0085/U+2028/U+2029 принимается (сканер отклонял) — категория D2.
- D6: `[Regex]`-паттерны пишутся raw-строками `"""..."""` (конвенция AGENTS.md + исключает
  экрани-ошибки C#-строк; первый subagent P2 уронил StringText через non-verbatim экрани).
