using System.Text.Json;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>Регламентное задание информационной базы (объект <c>РегламентноеЗадание</c>).</summary>
public sealed class InfobaseScheduledJob
{
    /// <summary>Уникальный идентификатор задания (строка UUID).</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Имя объекта метаданных регламентного задания.</summary>
    public string MetadataName { get; set; } = string.Empty;

    /// <summary>Синоним объекта метаданных (представление в конфигураторе).</summary>
    public string MetadataSynonym { get; set; } = string.Empty;

    /// <summary>Наименование задания.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ключ задания.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Задание включено (Использование).</summary>
    public bool Use { get; set; }

    /// <summary>Предопределённое задание.</summary>
    public bool Predefined { get; set; }

    /// <summary>Представление расписания.</summary>
    public string Schedule { get; set; } = string.Empty;

    /// <summary>Состояние последнего выполнения (если платформа его отдаёт).</summary>
    public string LastState { get; set; } = string.Empty;

    /// <summary>Начало последнего выполнения.</summary>
    public string LastStart { get; set; } = string.Empty;

    /// <summary>Описание ошибки последнего выполнения.</summary>
    public string LastError { get; set; } = string.Empty;

    /// <summary>Отображаемое имя: наименование, иначе синоним, иначе имя метаданных.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Description) ? Description
        : !string.IsNullOrWhiteSpace(MetadataSynonym) ? MetadataSynonym
        : MetadataName;
}

/// <summary>Результат операции над регламентными заданиями.</summary>
public sealed class ScheduledJobsResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public List<InfobaseScheduledJob> Jobs { get; init; } = new();

    /// <summary>Сколько заданий изменено (операции «включить/выключить»).</summary>
    public int Changed { get; init; }

    /// <summary>Задание найдено (операция над одним заданием).</summary>
    public bool Found { get; init; } = true;

    public static ScheduledJobsResult Fail(string? error) => new() { Error = error };
}

/// <summary>
/// Регламентные задания информационной базы через COM-коннектор (выпуск 1.0.0.85):
/// список заданий с расписанием и итогом последнего выполнения, включение и
/// выключение одного задания, массовое выключение/включение всех заданий базы
/// (например, в тестовой копии, чтобы она не слала обмены и письма).
/// Выполняется во внешнем процессе Windows PowerShell (<see cref="ComScriptRunner"/>).
/// </summary>
public static class ScheduledJobsService
{
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Тело скрипта: операции <c>list</c>, <c>setuse</c> (одно задание по UUID) и
    /// <c>setall</c> (все задания; <c>keep</c> — имена метаданных, которые не трогать).
    /// Свойства последнего выполнения читаются «мягко»: в части версий платформы
    /// <c>LastJob</c> во внешнем соединении недоступен.
    /// </summary>
    public const string OperationBody = @"
function Try-G($o,$n){try{G $o $n}catch{$null}}
function JInfo($c,$j){
$md=G $j 'Metadata'
$i=@{uuid=[string](M $c 'String' (G $j 'UUID'));name=[string](G $md 'Name');synonym=[string](Try-G $md 'Synonym');descr=[string](G $j 'Description');key=[string](G $j 'Key');use=[bool](G $j 'Use');predef=[bool](G $j 'Predefined');schedule='';state='';start='';error=''}
try{$i.schedule=[string](M $c 'String' (G $j 'Schedule'))}catch{}
$lj=Try-G $j 'LastJob'
if($null -ne $lj -and $lj -is [System.__ComObject]){
try{$i.state=[string](M $c 'String' (G $lj 'State'))}catch{}
try{$i.start=[string](M $c 'String' (G $lj 'Begin'))}catch{}
try{$ei=Try-G $lj 'ErrorInfo';if($null -ne $ei -and $ei -is [System.__ComObject]){$i.error=[string](G $ei 'Description')}}catch{}}
$i}
function Invoke-Op($c,$q){
$sj=G $c 'ScheduledJobs'
switch([string]$q.op){
'list'{
$arr=M $sj 'GetScheduledJobs';$n=M $arr 'Count';$list=New-Object System.Collections.ArrayList
for($i=0;$i -lt $n;$i++){[void]$list.Add((JInfo $c (M $arr 'Get' $i)))}
return @{jobs=$list}}
'setuse'{
$u=M $c 'NewObject' 'UUID' ([string]$q.uuid);$j=M $sj 'FindByUUID' $u
if($null -eq $j -or $j -isnot [System.__ComObject]){return @{found=$false;changed=0}}
$ch=0;if([bool](G $j 'Use') -ne [bool]$q.use){S $j 'Use' ([bool]$q.use);[void](M $j 'Write');$ch=1}
return @{found=$true;changed=$ch}}
'setall'{
$keep=@($q.keep|ForEach-Object{[string]$_})
$arr=M $sj 'GetScheduledJobs';$n=M $arr 'Count';$ch=0
for($i=0;$i -lt $n;$i++){$j=M $arr 'Get' $i;$name=[string](G (G $j 'Metadata') 'Name')
if($keep -contains $name){continue}
if([bool](G $j 'Use') -ne [bool]$q.use){S $j 'Use' ([bool]$q.use);[void](M $j 'Write');$ch++}}
return @{changed=$ch;total=$n}}
default{throw ('Unknown operation: '+[string]$q.op)}
}}
";

    /// <summary>Разбор ответа операции «список» (сортировка по отображаемому имени).</summary>
    public static List<InfobaseScheduledJob> ParseJobs(JsonElement data)
    {
        var jobs = ComScriptRunner.GetObjectList(data, "jobs").Select(j => new InfobaseScheduledJob
        {
            Uuid = ComScriptRunner.GetString(j, "uuid") ?? string.Empty,
            MetadataName = ComScriptRunner.GetString(j, "name") ?? string.Empty,
            MetadataSynonym = ComScriptRunner.GetString(j, "synonym") ?? string.Empty,
            Description = ComScriptRunner.GetString(j, "descr") ?? string.Empty,
            Key = ComScriptRunner.GetString(j, "key") ?? string.Empty,
            Use = ComScriptRunner.GetBool(j, "use"),
            Predefined = ComScriptRunner.GetBool(j, "predef"),
            Schedule = ComScriptRunner.GetString(j, "schedule") ?? string.Empty,
            LastState = ComScriptRunner.GetString(j, "state") ?? string.Empty,
            LastStart = ComScriptRunner.GetString(j, "start") ?? string.Empty,
            LastError = ComScriptRunner.GetString(j, "error") ?? string.Empty
        }).ToList();
        jobs.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return jobs;
    }

    /// <summary>Число из свойства JSON (0, если нет).</summary>
    public static int GetInt(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : 0;

    /// <summary>Список имён метаданных из строки «Имя1, Имя2» (исключения массового выключения).</summary>
    public static List<string> ParseNames(string? text) => InfobaseUsersService.ParseRoles(text);

    public static Task<ScheduledJobsResult> ListAsync(Infobase infobase, CancellationToken ct = default) =>
        RunAsync(infobase, new Dictionary<string, object?> { ["op"] = "list" },
            data => new ScheduledJobsResult { Success = true, Jobs = ParseJobs(data) }, ct);

    public static Task<ScheduledJobsResult> SetUseAsync(Infobase infobase, string uuid, bool use, CancellationToken ct = default) =>
        RunAsync(infobase, new Dictionary<string, object?> { ["op"] = "setuse", ["uuid"] = uuid, ["use"] = use },
            data => new ScheduledJobsResult
            {
                Success = true,
                Found = ComScriptRunner.GetBool(data, "found", true),
                Changed = GetInt(data, "changed")
            }, ct);

    /// <summary>Включает/выключает все задания базы, кроме перечисленных в <paramref name="keep"/>.</summary>
    public static Task<ScheduledJobsResult> SetAllAsync(Infobase infobase, bool use, IReadOnlyList<string>? keep = null,
        CancellationToken ct = default) =>
        RunAsync(infobase, new Dictionary<string, object?>
            {
                ["op"] = "setall",
                ["use"] = use,
                ["keep"] = keep ?? Array.Empty<string>()
            },
            data => new ScheduledJobsResult { Success = true, Changed = GetInt(data, "changed") }, ct);

    private static async Task<ScheduledJobsResult> RunAsync(Infobase infobase, Dictionary<string, object?> parameters,
        Func<JsonElement, ScheduledJobsResult> map, CancellationToken ct)
    {
        if (infobase is null)
            return ScheduledJobsResult.Fail(LocalizationManager.T("IbUsers.NoBase"));
        var connection = ComConnectionInfo.Prepare(infobase);
        if (connection.Error is not null)
            return ScheduledJobsResult.Fail(connection.Error);
        var result = await ComScriptRunner.RunAsync(OperationBody, connection.ProgIds, connection.ConnectString,
            parameters, new[] { connection.Password }, OperationTimeout, ct).ConfigureAwait(false);
        return result.Success ? map(result.Data) : ScheduledJobsResult.Fail(InfobaseUsersService.DescribeFailure(result));
    }
}
