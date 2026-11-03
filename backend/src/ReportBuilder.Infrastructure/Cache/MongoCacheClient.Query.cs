using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Export;

namespace ReportBuilder.Infrastructure.Cache;

public sealed partial class MongoCacheClient
{
    public async Task<ReportResult> QueryReportAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows,
        CancellationToken cancellationToken,
        int? maxTake = null)
    {
        var plan = DocumentQueryPlanner.ForReport(dataset, layout, skip, take, allRows, maxTake);
        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        if (plan.Grouping)
        {
            return await QueryGroupedAsync(collection, shape, plan, cancellationToken);
        }

        return await QueryUngroupedAsync(collection, shape, plan, cancellationToken);
    }

    public async Task<ColumnValuesResult> QueryDistinctAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        string field,
        int take,
        IEnumerable<LayoutFilter>? filters,
        CancellationToken cancellationToken)
    {
        var plan = DocumentQueryPlanner.ForDistinct(dataset, field, take, filters);
        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var path = shape.Path(plan.Selections[0].Column.ColumnName);
        var match = Builders<BsonDocument>.Filter.And(
            BuildFilter(shape, plan),
            Builders<BsonDocument>.Filter.Ne(path, BsonNull.Value),
            Builders<BsonDocument>.Filter.Ne(path, ""));

        var pipeline = new EmptyPipelineDefinition<BsonDocument>()
            .Match(match)
            .Group(new BsonDocument("_id", "$" + path))
            .Sort(plan.Sorts[0].Descending
                ? Builders<BsonDocument>.Sort.Descending("_id")
                : Builders<BsonDocument>.Sort.Ascending("_id"))
            .Limit(plan.Take + 1);

        var docs = await collection.Aggregate(pipeline, new AggregateOptions
        {
            AllowDiskUse = true
        }).ToListAsync(cancellationToken);

        var values = new List<string>();
        foreach (var doc in docs)
        {
            if (doc.GetValue("_id", BsonNull.Value).IsBsonNull)
            {
                continue;
            }

            var formatted = FormatDistinct(FromBson(doc["_id"]));
            if (string.IsNullOrWhiteSpace(formatted))
            {
                continue;
            }

            values.Add(formatted);
        }

        var truncated = values.Count > plan.Take;
        if (truncated)
        {
            values.RemoveRange(plan.Take, values.Count - plan.Take);
        }

        return new ColumnValuesResult
        {
            Field = field,
            Values = values,
            Truncated = truncated
        };
    }

    public async Task<ReportExportTable> QueryExportAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? exportedBy,
        string? lang,
        CancellationToken cancellationToken)
    {
        var allRows = format == ReportExportFormat.Xlsx;
        var cap = ReportExportLimits.PrintableRowCap;
        var plan = DocumentQueryPlanner.ForReport(
            dataset,
            layout,
            skip: 0,
            take: allRows ? 0 : cap,
            allRows: allRows,
            maxTake: allRows ? null : cap);
        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        ReportResult result;
        if (plan.Grouping)
        {
            result = await QueryGroupedAsync(collection, shape, plan, cancellationToken);
        }
        else
        {
            result = await QueryUngroupedAsync(collection, shape, plan, cancellationToken);
        }

        var table = SnapshotQueryExecutor.ToExportTable(result, layout, reportName, format, exportedBy, lang);
        if (!table.Truncated && result.TotalCount > result.Rows.Count)
        {
            return new ReportExportTable
            {
                Title = table.Title,
                Columns = table.Columns,
                ColumnStyles = table.ColumnStyles,
                Rows = table.Rows,
                Truncated = true,
                ExportedBy = table.ExportedBy
            };
        }

        return table;
    }

    public async Task WriteCsvAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        ReportLayout layout,
        Stream output,
        string? lang,
        CancellationToken cancellationToken)
    {
        var plan = DocumentQueryPlanner.ForReport(dataset, layout, skip: 0, take: 0, allRows: true);
        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var included = new List<(PlanSelection Selection, int Index)>();
        for (var i = 0; i < plan.Selections.Count; i++)
        {
            if (plan.Selections[i].Hidden || !LayoutColumnLocales.ShouldInclude(i, layout, lang))
            {
                continue;
            }

            included.Add((plan.Selections[i], i));
        }

        if (included.Count == 0)
        {
            throw new ReportQueryException("Select at least one column.");
        }

        await ReportExportWriter.WriteUtf8BomAsync(output, cancellationToken);
        await using var writer = ReportExportWriter.CreateCsvWriter(output);
        await ReportExportWriter.WriteCsvRecordAsync(
            writer,
            included.Select(item => item.Selection.ResultName).ToList(),
            cancellationToken);

        if (plan.Grouping)
        {
            await WriteGroupedCsvAsync(collection, shape, plan, included, layout, writer, cancellationToken);
        }
        else
        {
            await WriteFindCsvAsync(collection, shape, plan, included, layout, writer, cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private async Task WriteFindCsvAsync(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan,
        IReadOnlyList<(PlanSelection Selection, int Index)> included,
        ReportLayout layout,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        var projection = Builders<BsonDocument>.Projection.Include("_id");
        foreach (var item in included)
        {
            projection = projection.Include(shape.Path(item.Selection.Column.ColumnName));
        }

        var sort = plan.ExplicitSort
            ? BuildFindSort(shape, plan)
            : Builders<BsonDocument>.Sort.Ascending("_id");
        var find = collection
            .Find(BuildFilter(shape, plan), new FindOptions
            {
                BatchSize = 2_000,
                MaxTime = ExportMaxTime,
                NoCursorTimeout = true
            })
            .Sort(sort)
            .Project(projection);

        using var cursor = await find.ToCursorAsync(cancellationToken);
        var written = 0;
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                var cells = new string[included.Count];
                for (var i = 0; i < included.Count; i++)
                {
                    var (selection, index) = included[i];
                    var raw = shape.ReadCell(document, selection.Column.ColumnName, FromBson);
                    var layoutColumn = index < layout.Columns.Count ? layout.Columns[index] : null;
                    cells[i] = ReportExportWriter.Sanitize(
                        ColumnValueFormatter.FormatLayout(raw, layoutColumn?.Format, layoutColumn, selection.ResultName));
                }

                await ReportExportWriter.WriteCsvRecordAsync(writer, cells, cancellationToken);
                written++;
            }

            if (written >= ExportFlushRows)
            {
                await writer.FlushAsync(cancellationToken);
                written = 0;
            }
        }
    }

    private async Task WriteGroupedCsvAsync(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan,
        IReadOnlyList<(PlanSelection Selection, int Index)> included,
        ReportLayout layout,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        var pipeline = BuildGroupPipeline(collection, shape, plan);
        using var cursor = await collection.AggregateAsync<BsonDocument>(
            pipeline,
            new AggregateOptions
            {
                AllowDiskUse = true,
                BatchSize = 2_000,
                MaxTime = ExportMaxTime
            },
            cancellationToken);

        var written = 0;
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                var cells = new string[included.Count];
                for (var i = 0; i < included.Count; i++)
                {
                    var (selection, index) = included[i];
                    object? raw = document.TryGetValue(selection.ResultName, out var cell) ? FromBson(cell) : null;
                    var layoutColumn = index < layout.Columns.Count ? layout.Columns[index] : null;
                    cells[i] = ReportExportWriter.Sanitize(
                        ColumnValueFormatter.FormatLayout(raw, layoutColumn?.Format, layoutColumn, selection.ResultName));
                }

                await ReportExportWriter.WriteCsvRecordAsync(writer, cells, cancellationToken);
                written++;
            }

            if (written >= ExportFlushRows)
            {
                await writer.FlushAsync(cancellationToken);
                written = 0;
            }
        }
    }

    private const int ExportFlushRows = 5_000;

    private static readonly TimeSpan ExportMaxTime = TimeSpan.FromHours(2);

    private List<BsonDocument> BuildGroupPipeline(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan)
    {
        var groupId = new BsonDocument();
        foreach (var field in plan.GroupBy)
        {
            groupId[SafeAlias(field)] = "$" + shape.Path(field);
        }

        var group = new BsonDocument("_id", groupId);
        var project = new BsonDocument("_id", 0);
        foreach (var selection in plan.Selections)
        {
            var alias = SafeAlias(selection.ResultName);
            var path = "$" + shape.Path(selection.Column.ColumnName);
            group[alias] = selection.Aggregate switch
            {
                "COUNT" => new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$and", new BsonArray
                    {
                        new BsonDocument("$ne", new BsonArray { path, BsonNull.Value }),
                        new BsonDocument("$ne", new BsonArray { path, "" })
                    }),
                    1,
                    0
                })),
                "SUM" => new BsonDocument("$sum", path),
                "AVG" => new BsonDocument("$avg", path),
                "MIN" => new BsonDocument("$min", path),
                "MAX" => new BsonDocument("$max", path),
                _ => new BsonDocument("$first", path)
            };
            project[selection.ResultName] = selection.Aggregate is null
                ? "$_id." + SafeAlias(selection.Column.ColumnName)
                : "$" + alias;
        }

        var sortDoc = new BsonDocument();
        var lookupFields = new BsonDocument();
        for (var index = 0; index < plan.Sorts.Count; index++)
        {
            var sort = plan.Sorts[index];
            if (sort.LookupText is null)
            {
                sortDoc[sort.Selection.ResultName] = sort.Descending ? -1 : 1;
                continue;
            }

            var alias = "__rb_ls" + index;
            lookupFields[alias] = LookupSwitch("$" + sort.Selection.ResultName, sort.LookupText);
            sortDoc[alias] = sort.Descending ? -1 : 1;
        }

        if (sortDoc.ElementCount == 0 && plan.Selections.Count > 0)
        {
            sortDoc[plan.Selections[0].ResultName] = 1;
        }

        var pipeline = new List<BsonDocument>
        {
            new("$match", RenderFilter(collection, BuildFilter(shape, plan))),
            new("$group", group),
            new("$project", project)
        };
        if (lookupFields.ElementCount > 0)
        {
            pipeline.Add(new BsonDocument("$addFields", lookupFields));
        }

        pipeline.Add(new BsonDocument("$sort", sortDoc));
        return pipeline;
    }

    private async Task<(IMongoCollection<BsonDocument> Collection, CacheShape Shape)> OpenAsync(
        CacheProviderConnection connection,
        string collectionName,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var meta = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", MetaId))
            .FirstOrDefaultAsync(cancellationToken);
        if (meta is null)
        {
            throw new ReportQueryException("The MongoDB dataset cache was not found. Refresh the cache and try again.");
        }

        var columns = meta.GetValue("columns", new BsonArray())
            .AsBsonArray
            .Select(item => item.AsString)
            .ToList();
        if (columns.Count == 0)
        {
            throw new ReportQueryException("The MongoDB dataset cache has no columns. Refresh the cache and try again.");
        }

        var format = meta.GetValue("format", "").IsString ? meta["format"].AsString : string.Empty;
        var named = string.Equals(format, NamedFormat, StringComparison.OrdinalIgnoreCase);
        return (collection, new CacheShape(columns, named));
    }

    private static FilterDefinition<BsonDocument> BuildFilter(CacheShape shape, DocumentQueryPlan plan)
    {
        var parts = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Ne("_id", MetaId)
        };

        foreach (var field in plan.Filters)
        {
            parts.Add(BuildFieldFilter(shape, field));
        }

        return parts.Count == 1 ? parts[0] : Builders<BsonDocument>.Filter.And(parts);
    }

    private static FilterDefinition<BsonDocument> BuildFieldFilter(CacheShape shape, PlanFieldFilter field)
    {
        var path = shape.Path(field.Column.ColumnName);
        var parts = new List<FilterDefinition<BsonDocument>>();
        var equalValues = new List<BsonValue>();

        foreach (var predicate in field.Predicates)
        {
            switch (predicate.Operator)
            {
                case "eq":
                case "in":
                    foreach (var item in DocumentQueryPlanner.SplitList(predicate.Value))
                    {
                        equalValues.AddRange(FilterValues(field.Column, item));
                    }

                    break;
                case "isnull":
                    parts.Add(Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq(path, BsonNull.Value),
                        Builders<BsonDocument>.Filter.Exists(path, false),
                        Builders<BsonDocument>.Filter.Eq(path, "")));
                    break;
                case "isnotnull":
                    parts.Add(Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Ne(path, BsonNull.Value),
                        Builders<BsonDocument>.Filter.Exists(path, true),
                        Builders<BsonDocument>.Filter.Ne(path, "")));
                    break;
                case "contains":
                    parts.Add(ContainsFilter(path, predicate.Value));
                    break;
                case "andin":
                {
                    var keys = new List<BsonValue>();
                    foreach (var item in LookupFilterKeys.Split(predicate.Value))
                    {
                        keys.AddRange(FilterValues(field.Column, item));
                    }

                    parts.Add(keys.Count == 0
                        ? Builders<BsonDocument>.Filter.Exists("__rb_lookup_miss", true)
                        : Builders<BsonDocument>.Filter.In(path, keys));
                    break;
                }
                case "andnin":
                {
                    var keys = new List<BsonValue>();
                    foreach (var item in LookupFilterKeys.Split(predicate.Value))
                    {
                        keys.AddRange(FilterValues(field.Column, item));
                    }

                    if (keys.Count == 0)
                    {
                        break;
                    }

                    parts.Add(Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Nin(path, keys),
                        Builders<BsonDocument>.Filter.Ne(path, BsonNull.Value),
                        Builders<BsonDocument>.Filter.Ne(path, "")));
                    break;
                }
                case "nomatch":
                    parts.Add(Builders<BsonDocument>.Filter.Exists("__rb_lookup_miss", true));
                    break;
                case "neq":
                case "gt":
                case "gte":
                case "lt":
                case "lte":
                {
                    var values = FilterValues(field.Column, predicate.Value);
                    var compare = values[0];
                    parts.Add(predicate.Operator switch
                    {
                        "neq" => Builders<BsonDocument>.Filter.Nin(path, values),
                        "gt" => Builders<BsonDocument>.Filter.Gt(path, compare),
                        "gte" => Builders<BsonDocument>.Filter.Gte(path, compare),
                        "lt" => Builders<BsonDocument>.Filter.Lt(path, compare),
                        "lte" => Builders<BsonDocument>.Filter.Lte(path, compare),
                        _ => Builders<BsonDocument>.Filter.Empty
                    });
                    break;
                }
                default:
                    throw new ReportQueryException($"Unsupported filter operator '{predicate.Operator}'.");
            }
        }

        if (equalValues.Count > 0)
        {
            parts.Insert(0, Builders<BsonDocument>.Filter.In(path, equalValues));
        }

        return parts.Count == 1 ? parts[0] : Builders<BsonDocument>.Filter.And(parts);
    }

    private static FilterDefinition<BsonDocument> ContainsFilter(string path, string? raw)
    {
        var needle = Regex.Escape(raw ?? string.Empty);
        return new BsonDocumentFilterDefinition<BsonDocument>(new BsonDocument(
            "$expr",
            new BsonDocument(
                "$regexMatch",
                new BsonDocument
                {
                    { "input", new BsonDocument("$toString", new BsonDocument("$ifNull", new BsonArray { "$" + path, "" })) },
                    { "regex", needle },
                    { "options", "i" }
                })));
    }

    private static List<BsonValue> FilterValues(DatasetColumn column, string? raw)
    {
        var converted = DocumentQueryPlanner.ConvertFilterValue(column, raw);
        var values = new List<BsonValue> { ToBson(converted) };
        if (converted is not string text)
        {
            var asText = Convert.ToString(converted, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(asText))
            {
                values.Add(asText);
            }
        }
        else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            values.Add(i);
        }

        return values;
    }

    private static SortDefinition<BsonDocument> BuildFindSort(CacheShape shape, DocumentQueryPlan plan)
    {
        var sorts = plan.Sorts
            .Select(sort =>
            {
                var path = shape.Path(sort.Selection.Column.ColumnName);
                return sort.Descending
                    ? Builders<BsonDocument>.Sort.Descending(path)
                    : Builders<BsonDocument>.Sort.Ascending(path);
            })
            .ToList();
        return sorts.Count == 0
            ? Builders<BsonDocument>.Sort.Ascending("_id")
            : Builders<BsonDocument>.Sort.Combine(sorts);
    }

    private async Task<ReportResult> QueryUngroupedAsync(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilter(shape, plan);
        int total;
        if (plan.Filters.Count == 0)
        {
            var estimated = await collection.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
            total = (int)Math.Min(int.MaxValue, Math.Max(0, estimated - 1));
        }
        else
        {
            total = (int)Math.Min(
                int.MaxValue,
                await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken));
        }

        if (plan.Sorts.Any(sort => sort.LookupText is not null))
        {
            return await QueryLookupSortedAsync(collection, shape, plan, filter, total, cancellationToken);
        }

        var sort = plan.ExplicitSort
            ? BuildFindSort(shape, plan)
            : Builders<BsonDocument>.Sort.Ascending("_id");
        var find = collection.Find(filter).Sort(sort);
        if (!plan.AllRows)
        {
            find = find.Skip(plan.Skip).Limit(plan.Take);
        }

        var docs = await find.ToListAsync(cancellationToken);
        return ToReportResult(docs, shape, plan, total);
    }

    private async Task<ReportResult> QueryLookupSortedAsync(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan,
        FilterDefinition<BsonDocument> filter,
        int total,
        CancellationToken cancellationToken)
    {
        var lookupFields = new BsonDocument();
        var sortDoc = new BsonDocument();
        for (var index = 0; index < plan.Sorts.Count; index++)
        {
            var sort = plan.Sorts[index];
            if (sort.LookupText is null)
            {
                sortDoc[shape.Path(sort.Selection.Column.ColumnName)] = sort.Descending ? -1 : 1;
                continue;
            }

            var alias = "__rb_ls" + index;
            lookupFields[alias] = LookupSwitch("$" + shape.Path(sort.Selection.Column.ColumnName), sort.LookupText);
            sortDoc[alias] = sort.Descending ? -1 : 1;
        }

        sortDoc["_id"] = 1;
        var pipeline = new List<BsonDocument>
        {
            new("$match", RenderFilter(collection, filter)),
            new("$addFields", lookupFields),
            new("$sort", sortDoc)
        };
        if (!plan.AllRows)
        {
            pipeline.Add(new BsonDocument("$skip", plan.Skip));
            pipeline.Add(new BsonDocument("$limit", plan.Take));
        }

        var docs = await collection.Aggregate<BsonDocument>(pipeline, new AggregateOptions
        {
            AllowDiskUse = true
        }).ToListAsync(cancellationToken);
        return ToReportResult(docs, shape, plan, total);
    }

    private static BsonValue LookupSwitch(string fieldPath, IReadOnlyDictionary<string, string> map)
    {
        var branches = new BsonArray();
        foreach (var pair in map)
        {
            branches.Add(new BsonDocument
            {
                {
                    "case",
                    new BsonDocument("$eq", new BsonArray
                    {
                        new BsonDocument("$toLower", new BsonDocument("$toString", fieldPath)),
                        pair.Key.ToLowerInvariant()
                    })
                },
                { "then", pair.Value ?? string.Empty }
            });
        }

        if (branches.Count == 0)
        {
            return BsonNull.Value;
        }

        return new BsonDocument("$switch", new BsonDocument
        {
            { "branches", branches },
            { "default", BsonNull.Value }
        });
    }

    private async Task<ReportResult> QueryGroupedAsync(
        IMongoCollection<BsonDocument> collection,
        CacheShape shape,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var pipeline = BuildGroupPipeline(collection, shape, plan);
        var pageStages = new List<BsonDocument>();
        if (!plan.AllRows)
        {
            pageStages.Add(new BsonDocument("$skip", plan.Skip));
            pageStages.Add(new BsonDocument("$limit", plan.Take));
        }

        pipeline.Add(new BsonDocument("$facet", new BsonDocument
        {
            { "total", new BsonArray { new BsonDocument("$count", "n") } },
            { "page", new BsonArray(pageStages) }
        }));

        var facet = await collection.Aggregate<BsonDocument>(pipeline, new AggregateOptions
        {
            AllowDiskUse = true
        }).FirstOrDefaultAsync(cancellationToken);

        var total = 0;
        var totalArray = facet?.GetValue("total", new BsonArray()).AsBsonArray;
        if (totalArray is { Count: > 0 })
        {
            total = totalArray[0].AsBsonDocument.GetValue("n", 0).ToInt32();
        }

        var page = facet?.GetValue("page", new BsonArray()).AsBsonArray ?? [];
        var rows = new List<IReadOnlyDictionary<string, object?>>(page.Count);
        foreach (var item in page)
        {
            var doc = item.AsBsonDocument;
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in plan.Selections)
            {
                row[selection.ResultName] = doc.TryGetValue(selection.ResultName, out var cell)
                    ? FromBson(cell)
                    : null;
            }

            rows.Add(row);
        }

        return new ReportResult
        {
            Columns = plan.Selections.Select(selection => new ReportResultColumn
            {
                Name = selection.ResultName,
                Label = string.IsNullOrWhiteSpace(selection.Alias) ? selection.ResultName : selection.Alias,
                Hidden = selection.Hidden
            }).ToList(),
            Rows = rows,
            TotalCount = total,
            Skip = plan.Skip,
            Take = plan.AllRows ? total : plan.Take
        };
    }

    private static ReportResult ToReportResult(
        IReadOnlyList<BsonDocument> docs,
        CacheShape shape,
        DocumentQueryPlan plan,
        int total)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>(docs.Count);
        foreach (var document in docs)
        {
            var raw = shape.ReadRow(document, FromBson);
            var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in plan.Selections)
            {
                projected[selection.ResultName] = raw.TryGetValue(selection.Column.ColumnName, out var value)
                    ? value
                    : null;
            }

            rows.Add(projected);
        }

        return new ReportResult
        {
            Columns = plan.Selections.Select(selection => new ReportResultColumn
            {
                Name = selection.ResultName,
                Label = string.IsNullOrWhiteSpace(selection.Alias) ? selection.ResultName : selection.Alias,
                Hidden = selection.Hidden
            }).ToList(),
            Rows = rows,
            TotalCount = total,
            Skip = plan.Skip,
            Take = plan.AllRows ? total : plan.Take
        };
    }

    private static BsonDocument RenderFilter(
        IMongoCollection<BsonDocument> collection,
        FilterDefinition<BsonDocument> filter)
        => filter.Render(new RenderArgs<BsonDocument>(
            collection.DocumentSerializer,
            collection.Settings.SerializerRegistry));

    private static string SafeAlias(string name)
    {
        var chars = name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var alias = new string(chars);
        return string.IsNullOrWhiteSpace(alias) ? "f" : alias;
    }

    private static string FormatDistinct(object? value)
        => value switch
        {
            null => string.Empty,
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private sealed class CacheShape
    {
        public CacheShape(IReadOnlyList<string> columns, bool named)
        {
            Columns = columns;
            Named = named;
            Index = columns
                .Select((name, index) => (name, index))
                .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> Columns { get; }
        public bool Named { get; }
        private Dictionary<string, int> Index { get; }

        public object? ReadCell(BsonDocument document, string column, Func<BsonValue, object?> fromBson)
        {
            if (!Index.TryGetValue(column, out var i))
            {
                return null;
            }

            if (Named)
            {
                return document.TryGetValue(FieldName(i), out var cell) ? fromBson(cell) : null;
            }

            var values = document.GetValue("v", new BsonArray()).AsBsonArray;
            return i < values.Count ? fromBson(values[i]) : null;
        }

        public string Path(string column)
        {
            if (!Index.TryGetValue(column, out var i))
            {
                throw new ReportQueryException($"Column '{column}' is not in the Mongo cache. Refresh the cache and try again.");
            }

            return Named ? FieldName(i) : $"v.{i}";
        }

        public Dictionary<string, object?> ReadRow(BsonDocument document, Func<BsonValue, object?> fromBson)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Columns.Count; i++)
            {
                if (Named)
                {
                    row[Columns[i]] = document.TryGetValue(FieldName(i), out var cell) ? fromBson(cell) : null;
                    continue;
                }

                var values = document.GetValue("v", new BsonArray()).AsBsonArray;
                row[Columns[i]] = i < values.Count ? fromBson(values[i]) : null;
            }

            return row;
        }
    }
}
