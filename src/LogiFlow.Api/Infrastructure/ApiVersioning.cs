using System.Globalization;

namespace LogiFlow.Api.Infrastructure;

/// <summary>
/// URL-segment API versioning, and the compatibility path for the routes that were published
/// before there were versions.
/// </summary>
/// <remarks>
/// <para>
/// <b>The question this answers</b> is the one every REST interview asks and most codebases
/// answer with a shrug: <i>how do you change a response that somebody is already consuming?</i>
/// You cannot. Not in place. A client parsing your JSON is coupled to its shape, and the only
/// thing separating "we added a field" from "we took production down" is whether the consumer
/// wrote a strict deserializer. So the shape gets a version, and the old shape keeps working
/// until the last caller has moved.
/// </para>
/// <para>
/// <b>Three ways to carry the version, and why this one.</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>URL segment</b> — <c>/api/v2/reports/sales</c>. Visible in a log, in a browser, in a
///     cURL command somebody pastes into a ticket. Cacheable per version by any intermediary
///     without configuration, because the URL <i>is</i> the cache key. Purists object that the
///     resource has not changed, only its representation, so the version does not belong in the
///     identifier — which is correct, and loses to the fact that everyone can see it.
///   </description></item>
///   <item><description>
///     <b>Custom header</b> — <c>X-Api-Version: 2</c>. Keeps URLs clean and is trivial to
///     default. It is also invisible: a caller on the wrong version debugs by guessing, and a
///     shared cache needs <c>Vary</c> configured correctly or it will serve one version's body
///     to the other version's client.
///   </description></item>
///   <item><description>
///     <b>Media type</b> — <c>Accept: application/vnd.logiflow.v2+json</c>. The most correct
///     by REST's own reasoning, since it is content negotiation doing what it is for. Also the
///     one that no client library makes convenient, which is why it is rare outside GitHub.
///   </description></item>
/// </list>
/// <para>
/// <b>What is deliberately not here.</b> The <c>Asp.Versioning.Http</c> package (formerly
/// <c>Microsoft.AspNetCore.Mvc.Versioning</c>) does all of this, plus reading the version from
/// any of the three sources at once, plus an OpenAPI document per version. It is the right
/// answer on a team, and naming it is the right answer in an interview. It is not used here for
/// the same reason the mediator in <c>Application/Abstractions/Messaging</c> is hand-written:
/// forty lines you can read beat a package you cannot, when the point is understanding what the
/// package does.
/// </para>
/// Covered in: <c>course/module-15-aspnetcore-in-depth/02-api-versioning.md</c>
/// </remarks>
public static class ApiVersioning
{
    /// <summary>Every version this service currently serves, oldest first.</summary>
    public static readonly string[] Supported = ["v1", "v2"];

    /// <summary>
    /// The date the unversioned <c>/api/...</c> routes stop being answered.
    /// </summary>
    /// <remarks>
    /// A date, not "soon". RFC 8594 defines the <c>Sunset</c> header precisely so that a
    /// removal can be announced in a way a machine can act on, and so that "we told you" is a
    /// checkable claim rather than a mailing list nobody read. Publishing a date you then
    /// extend is survivable; publishing no date means the old shape is permanent.
    /// </remarks>
    public static readonly DateTimeOffset UnversionedSunset = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string SupportedVersionsHeader = "api-supported-versions";
    private const string DeprecationHeader = "Deprecation";
    private const string SunsetHeader = "Sunset";

    /// <summary>
    /// Segments under <c>/api</c> that are not versioned resources and must be left alone by
    /// the compatibility rewrite below.
    /// </summary>
    private static readonly string[] NotResources = ["dev"];

    /// <summary>
    /// Creates the route group for one version — <c>/api/v1</c>, <c>/api/v2</c> — and tags every
    /// response in it with the versions this service supports.
    /// </summary>
    /// <param name="app">The endpoint builder.</param>
    /// <param name="version">The version segment, for example <c>v2</c>.</param>
    /// <returns>The group. Map endpoints onto it with paths that do not repeat the prefix.</returns>
    public static RouteGroupBuilder MapApiVersion(this IEndpointRouteBuilder app, string version)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.MapGroup($"/api/{version}")
            // Advertise the full set on every response. A client that hard-codes v1 can then
            // discover that v2 exists without reading documentation it was never sent, which is
            // the difference between a deprecation people notice and one they do not.
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers[SupportedVersionsHeader] = string.Join(", ", Supported);
                return await next(context);
            });
    }

    /// <summary>
    /// Keeps the original unversioned routes — <c>/api/orders</c> — working by rewriting them
    /// onto <c>/api/v1</c>, and marks every such response as deprecated.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>A rewrite, not a redirect</b>, and the difference is worth having an opinion about
    /// because it is a real interview question.
    /// </para>
    /// <para>
    /// A <c>308 Permanent Redirect</c> would be more honest: the client learns the new URL, and
    /// a well-behaved one updates its own records. It also costs a second round trip on every
    /// call, and it moves the risk onto the client's HTTP stack — <c>308</c> preserves the
    /// method and body only if the client implements it correctly and only if the body can be
    /// replayed, which a non-buffered upload stream cannot. A caller posting a large payload
    /// over a slow link discovers this as an intermittent failure.
    /// </para>
    /// <para>
    /// The rewrite is invisible and free: the path is changed before routing runs, so exactly
    /// one set of endpoints exists and there is no duplicate registration to keep in step. What
    /// it gives up is the client's chance to learn, which is why the response still carries
    /// <c>Deprecation</c> and <c>Sunset</c>. Redirect when you want callers to move; rewrite
    /// when you want them not to break. Both, in sequence, is how a large API actually migrates.
    /// </para>
    /// <para>
    /// This must run <b>before</b> routing, because routing matches on the path and the whole
    /// point is to change the path first.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseUnversionedApiCompatibility(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (TryRewrite(context.Request.Path, out PathString rewritten))
            {
                context.Request.Path = rewritten;

                context.Response.Headers[DeprecationHeader] = "true";
                context.Response.Headers[SunsetHeader] = UnversionedSunset.ToString("R", CultureInfo.InvariantCulture);

                // RFC 8288: point at the thing that replaces this. `deprecation` links to the
                // explanation, `successor-version` to the URL to use instead.
                context.Response.Headers.Link =
                    $"<{context.Request.Scheme}://{context.Request.Host}{rewritten}>; rel=\"successor-version\"";
            }

            await next(context);
        });
    }

    /// <summary>
    /// Decides whether a path is an unversioned API route, and what it becomes.
    /// </summary>
    /// <param name="path">The incoming request path.</param>
    /// <param name="rewritten">The versioned equivalent, when there is one.</param>
    /// <returns><see langword="true"/> when the path was unversioned and should be rewritten.</returns>
    /// <remarks>
    /// Internal rather than private so the unit tests can drive the decision directly. Routing
    /// rules are the kind of thing where the interesting cases are the ones nobody thinks to
    /// send through a running server: <c>/api</c> on its own, <c>/api/v1</c> already versioned,
    /// and <c>/api/version-history</c>, which starts with the letter v and is not a version.
    /// </remarks>
    internal static bool TryRewrite(PathString path, out PathString rewritten)
    {
        rewritten = default;

        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase, out PathString rest)
            || !rest.HasValue)
        {
            return false;
        }

        string first = rest.Value!.TrimStart('/').Split('/')[0];

        if (first.Length == 0 || NotResources.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Already versioned. Matched exactly against the supported list rather than by shape,
        // so that a future resource called `videos` is not mistaken for a version.
        if (Supported.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        rewritten = new PathString($"/api/{Supported[0]}{rest}");
        return true;
    }
}
