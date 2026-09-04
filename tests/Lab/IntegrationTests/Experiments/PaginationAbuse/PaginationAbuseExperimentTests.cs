using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.PaginationAbuse;

/// <summary>
/// Observation experiment: a deterministic careless client against
/// <c>GET /api/v2/products-page</c>. Not a pagination correctness suite.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class PaginationAbuseExperimentTests
{
	private const int Limit = 7;
	private const int WalkPages = 8;
	private const int TamperDelta = 50;

	private readonly CarelessPaginationClient _client;
	private readonly ITestOutputHelper _output;

	public PaginationAbuseExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_client = new CarelessPaginationClient(fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		}));
	}

	[Fact]
	public async Task Cursor_abuse_against_products_page_is_observed()
	{
		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var calls = new List<PaginationAbuseCall>();
		var requestNumber = 0;

		async Task<PaginationAbuseCall> SendAsync(string behavior, string cursor)
		{
			requestNumber++;
			var (traceId, spanId) = NewTraceParent();
			var http = await _client.GetAsync(
				Limit,
				cursor,
				CarelessPaginationClient.PageDirection,
				FormatTraceParent(traceId, spanId));
			var page = http.Page;
			var spans = capture.ForTrace(traceId);
			var mediator = spans.Where(IsMediator).ToList();
			return new PaginationAbuseCall(
				requestNumber,
				behavior,
				Limit,
				CarelessPaginationClient.SortBy,
				CarelessPaginationClient.SortDirection,
				CarelessPaginationClient.PageDirection,
				cursor,
				http.HttpStatus,
				page?.Items.Select(i => i.Id).ToArray() ?? [],
				page?.NextCursor ?? "",
				page?.PreviousCursor ?? "",
				page?.HasMore ?? false,
				page?.HasPrevious ?? false,
				page?.TotalCount ?? 0,
				http.ClientDurationMs,
				http.Error,
				traceId.ToHexString(),
				mediator.Count,
				mediator.Count == 0 ? null : mediator.Max(s => s.Duration.TotalMilliseconds),
				spans.Count(IsNpgsql));
		}

		for (var i = 0; i < WalkPages; i++)
		{
			var cursorIn = i == 0 ? "" : calls[^1].NextCursor;
			calls.Add(await SendAsync("Walk", cursorIn));
		}

		var walks = calls.Where(c => c.Behavior == "Walk").ToList();
		walks.Should().HaveCount(WalkPages);

		var walk2 = walks[1];
		var replay = await SendAsync("Replay", walk2.CursorIn);
		calls.Add(replay);

		var stale = await SendAsync("StaleReuse", walk2.CursorIn);
		calls.Add(stale);

		var tamperSource = walks[0].NextCursor;
		tamperSource.Should().NotBeNullOrWhiteSpace();
		var forged = CarelessPaginationClient.TamperSeekId(tamperSource, TamperDelta);
		var tamper = await SendAsync("Tamper", forged);
		calls.Add(tamper);

		var malformed = await SendAsync("MalformedProbe", "not-a-valid-cursor");
		calls.Add(malformed);

		var walkIds = walks.SelectMany(c => c.ItemIds).ToList();
		var walkUnique = walkIds.Distinct().ToList();
		var replaySame = replay.ItemIds.SequenceEqual(walk2.ItemIds);
		var staleSame = stale.ItemIds.SequenceEqual(walk2.ItemIds);
		var cursorIns = walks.Select(c => c.CursorIn).ToList();
		var cursorLoop = cursorIns
			.Select((c, i) => (c, i))
			.Any(x => x.i > 0 && cursorIns.Take(x.i).Contains(x.c, StringComparer.Ordinal));

		var observations = new PaginationAbuseObservations(
			WalkCalls: walks.Count,
			WalkUniqueIds: walkUnique.Count,
			WalkDuplicateIds: walkIds.Count - walkUnique.Count,
			WalkEmptyPages: walks.Count(c => c.ItemIds.Count == 0),
			ReplaySameIdsAsOriginal: replaySame,
			ReplayDuplicateIdCount: replay.ItemIds.Intersect(walk2.ItemIds).Count(),
			StaleReuseSameIdsAsOriginalPage: staleSame,
			TamperHttpStatus: tamper.HttpStatus,
			TamperReturnedIds: tamper.ItemIds,
			MalformedHttpStatus: malformed.HttpStatus,
			CursorLoopDetected: cursorLoop,
			TotalCalls: calls.Count,
			FailureCount: calls.Count(c => c.HttpStatus >= 400),
			TotalClientDurationMs: calls.Sum(c => c.ClientDurationMs),
			Notes:
			[
				$"Replay ids {(replaySame ? "matched" : "did not match")} walk call #2.",
				$"Stale reuse ids {(staleSame ? "matched" : "did not match")} walk call #2 (seek position vs session ticket).",
				$"Tamper HTTP {tamper.HttpStatus}; seek Id bumped by +{TamperDelta}; returned [{string.Join(",", tamper.ItemIds)}].",
				$"Walk npgsql spans: {string.Join(",", walks.Select(c => c.NpgsqlSpanCount))}.",
				$"First-page totalCount={walks[0].TotalCount}."
			]);

		var result = new PaginationAbuseExperimentResult(
			Name: "pagination-cursor-abuse-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			CatalogProductCount: walks[0].TotalCount,
			Configuration: new PaginationAbuseConfiguration(
				CarelessPaginationClient.Path,
				CarelessPaginationClient.SortBy,
				CarelessPaginationClient.SortDirection,
				Limit,
				WalkPages),
			Calls: calls,
			Observations: observations);

		_output.WriteLine(JsonSerializer.Serialize(result, PaginationAbuseJsonContext.Default.PaginationAbuseExperimentResult));

		foreach (var walk in walks)
		{
			walk.HttpStatus.Should().Be(200);
			walk.ItemIds.Should().HaveCount(Limit, "non-final walk pages of a 1000-row catalog should be full");
		}

		walks[0].HasPrevious.Should().BeFalse();
		walks[0].TotalCount.Should().BeGreaterThan(Limit);
		malformed.HttpStatus.Should().Be(400);
	}
}
