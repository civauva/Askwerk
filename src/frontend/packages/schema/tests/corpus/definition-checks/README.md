<!-- SPDX-License-Identifier: Apache-2.0 -->
# Definition checks corpus

Golden cases for the checks that run before a survey is published (M3 in AWK-5). Both the TypeScript checks
(`src/definition-checks.ts`) and the C# checks (`Askwerk.Domain/Surveys/DefinitionChecks.cs`) run every file here
and must report exactly the `expected` issues, in document order. A case is added here before the code that handles it.

Each file: `{ "name", "definition", "previous"?, "expected": [{ "code", "severity", "path", "language"? }] }`.
`previous` is the last published definition, when the case is about renames.
