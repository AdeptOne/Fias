namespace Fias.Eval;

/// <summary>
/// Один размеченный пример золотого набора. «Разметка» бесплатная: <see cref="ExpectedGuid"/>
/// взят из самой проекции search.* — это и есть ground-truth ответ на синтезированный запрос.
/// </summary>
/// <param name="Bucket">Категория запроса (clean/reorder/typo/abbrev/house/type) — для срезов метрик.</param>
/// <param name="Query">Синтезированная пользовательская строка, как её ввели бы (грязная форма).</param>
/// <param name="ExpectedGuid">Эталонный FIAS GUID, который поиск обязан вернуть.</param>
/// <param name="ExpectedLabel">Человекочитаемый эталон (для CSV промахов).</param>
public sealed record GoldenItem(string Bucket, string Query, Guid ExpectedGuid, string ExpectedLabel);
