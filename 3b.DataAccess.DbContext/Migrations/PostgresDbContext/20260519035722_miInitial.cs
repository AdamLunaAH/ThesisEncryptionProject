using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccess.DbContext.Migrations.PostgresDbContext
{
    /// <inheritdoc />
    public partial class miInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "benchmark");

            migrationBuilder.CreateTable(
                name: "BenchmarkRun",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlgorithmId = table.Column<string>(type: "varchar(200)", nullable: false),
                    AlgorithmFamily = table.Column<string>(type: "varchar(200)", nullable: false),
                    Generation = table.Column<string>(type: "varchar(200)", nullable: false),
                    AuthId = table.Column<string>(type: "varchar(200)", nullable: true),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    MessageSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    WarmupCount = table.Column<int>(type: "integer", nullable: false),
                    TlsVersion = table.Column<string>(type: "varchar(200)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(200)", nullable: true),
                    RunNumber = table.Column<int>(type: "integer", nullable: false),
                    AlgorithmRunNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRun", x => x.BenchmarkRunId);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkMessagePayload",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkMessagePayloadId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageIndex = table.Column<int>(type: "integer", nullable: false),
                    AlgorithmId = table.Column<string>(type: "varchar(200)", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncapsulatedKey = table.Column<byte[]>(type: "bytea", nullable: true),
                    MacTag = table.Column<byte[]>(type: "bytea", nullable: true),
                    PlaintextBytes = table.Column<int>(type: "integer", nullable: false),
                    TotalWireBytes = table.Column<int>(type: "integer", nullable: false),
                    FilePath = table.Column<string>(type: "varchar(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkMessagePayload", x => x.BenchmarkMessagePayloadId);
                    table.ForeignKey(
                        name: "FK_BenchmarkMessagePayload_BenchmarkRun_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalSchema: "benchmark",
                        principalTable: "BenchmarkRun",
                        principalColumn: "BenchmarkRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkMessageResult",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkMessageResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageIndex = table.Column<int>(type: "integer", nullable: false),
                    EncryptMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    DecryptMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    SignMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    VerifyMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    SignalRTransitMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    TotalRoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    PlaintextBytes = table.Column<int>(type: "integer", nullable: false),
                    CiphertextBytes = table.Column<int>(type: "integer", nullable: false),
                    EncapsulatedKeyBytes = table.Column<int>(type: "integer", nullable: false),
                    MacTagBytes = table.Column<int>(type: "integer", nullable: false),
                    TotalWireBytes = table.Column<int>(type: "integer", nullable: false),
                    EncryptionOverheadBytes = table.Column<int>(type: "integer", nullable: false),
                    GcAllocatedBytes = table.Column<long>(type: "bigint", nullable: false),
                    GcGen0Collections = table.Column<int>(type: "integer", nullable: false),
                    DecryptSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    VerifySuccess = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkMessageResult", x => x.BenchmarkMessageResultId);
                    table.ForeignKey(
                        name: "FK_BenchmarkMessageResult_BenchmarkRun_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalSchema: "benchmark",
                        principalTable: "BenchmarkRun",
                        principalColumn: "BenchmarkRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkResourceAggregate",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkResourceAggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMilliseconds = table.Column<double>(type: "double precision", nullable: false),
                    CpuMax = table.Column<double>(type: "double precision", nullable: false),
                    CpuAvg = table.Column<double>(type: "double precision", nullable: false),
                    CpuP50 = table.Column<double>(type: "double precision", nullable: false),
                    CpuP95 = table.Column<double>(type: "double precision", nullable: false),
                    CpuP99 = table.Column<double>(type: "double precision", nullable: false),
                    MemoryMaxMb = table.Column<double>(type: "double precision", nullable: false),
                    MemoryAvgMb = table.Column<double>(type: "double precision", nullable: false),
                    MemoryP50Mb = table.Column<double>(type: "double precision", nullable: false),
                    MemoryP95Mb = table.Column<double>(type: "double precision", nullable: false),
                    MemoryP99Mb = table.Column<double>(type: "double precision", nullable: false),
                    GcGen0Delta = table.Column<int>(type: "integer", nullable: false),
                    GcGen1Delta = table.Column<int>(type: "integer", nullable: false),
                    GcGen2Delta = table.Column<int>(type: "integer", nullable: false),
                    ThreadPoolMaxWorkers = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkResourceAggregate", x => x.BenchmarkResourceAggregateId);
                    table.ForeignKey(
                        name: "FK_BenchmarkResourceAggregate_BenchmarkRun_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalSchema: "benchmark",
                        principalTable: "BenchmarkRun",
                        principalColumn: "BenchmarkRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkResourceSample",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkResourceSampleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CpuPercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryUsedMb = table.Column<double>(type: "double precision", nullable: false),
                    GcHeapMb = table.Column<double>(type: "double precision", nullable: false),
                    GcGen0Collections = table.Column<int>(type: "integer", nullable: false),
                    GcGen1Collections = table.Column<int>(type: "integer", nullable: false),
                    GcGen2Collections = table.Column<int>(type: "integer", nullable: false),
                    ThreadPoolWorkerThreads = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkResourceSample", x => x.BenchmarkResourceSampleId);
                    table.ForeignKey(
                        name: "FK_BenchmarkResourceSample_BenchmarkRun_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalSchema: "benchmark",
                        principalTable: "BenchmarkRun",
                        principalColumn: "BenchmarkRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkSessionAggregate",
                schema: "benchmark",
                columns: table => new
                {
                    BenchmarkSessionAggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvgEncryptMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgDecryptMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgSignMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgVerifyMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgSignalRTransitMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgTotalRoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    MinRoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    MaxRoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    P50RoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    P95RoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    P99RoundTripMicroseconds = table.Column<double>(type: "double precision", nullable: false),
                    AvgCiphertextBytes = table.Column<double>(type: "double precision", nullable: false),
                    AvgEncapsulatedKeyBytes = table.Column<double>(type: "double precision", nullable: false),
                    AvgMacTagBytes = table.Column<double>(type: "double precision", nullable: false),
                    AvgTotalWireBytes = table.Column<double>(type: "double precision", nullable: false),
                    AvgEncryptionOverheadBytes = table.Column<double>(type: "double precision", nullable: false),
                    TotalGcAllocatedBytes = table.Column<long>(type: "bigint", nullable: false),
                    AvgGcAllocatedBytesPerMessage = table.Column<double>(type: "double precision", nullable: false),
                    TotalGcGen0Collections = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulMessages = table.Column<int>(type: "integer", nullable: false),
                    SuccessRate = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkSessionAggregate", x => x.BenchmarkSessionAggregateId);
                    table.ForeignKey(
                        name: "FK_BenchmarkSessionAggregate_BenchmarkRun_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalSchema: "benchmark",
                        principalTable: "BenchmarkRun",
                        principalColumn: "BenchmarkRunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkMessagePayload_BenchmarkRunId",
                schema: "benchmark",
                table: "BenchmarkMessagePayload",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkMessageResult_BenchmarkRunId",
                schema: "benchmark",
                table: "BenchmarkMessageResult",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkResourceAggregate_BenchmarkRunId",
                schema: "benchmark",
                table: "BenchmarkResourceAggregate",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkResourceSample_BenchmarkRunId",
                schema: "benchmark",
                table: "BenchmarkResourceSample",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSessionAggregate_BenchmarkRunId",
                schema: "benchmark",
                table: "BenchmarkSessionAggregate",
                column: "BenchmarkRunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkMessagePayload",
                schema: "benchmark");

            migrationBuilder.DropTable(
                name: "BenchmarkMessageResult",
                schema: "benchmark");

            migrationBuilder.DropTable(
                name: "BenchmarkResourceAggregate",
                schema: "benchmark");

            migrationBuilder.DropTable(
                name: "BenchmarkResourceSample",
                schema: "benchmark");

            migrationBuilder.DropTable(
                name: "BenchmarkSessionAggregate",
                schema: "benchmark");

            migrationBuilder.DropTable(
                name: "BenchmarkRun",
                schema: "benchmark");
        }
    }
}
