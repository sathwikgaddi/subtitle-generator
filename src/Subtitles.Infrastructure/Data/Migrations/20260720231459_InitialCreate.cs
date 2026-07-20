using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subtitles.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    plan_tier = table.Column<string>(type: "text", nullable: false, defaultValue: "free"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    template = table.Column<string>(type: "text", nullable: false),
                    model_params_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "videos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: false),
                    blob_path = table.Column<string>(type: "text", nullable: false),
                    audio_blob_path = table.Column<string>(type: "text", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detected_language_code = table.Column<string>(type: "text", nullable: true),
                    detected_language_confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_videos", x => x.id);
                    table.ForeignKey(
                        name: "fk_videos_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_videos_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subtitle_track_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    blob_path = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exports", x => x.id);
                    table.ForeignKey(
                        name: "fk_exports_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_by = table.Column<string>(type: "text", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_processing_jobs_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subtitle_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    language_code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subtitle_tracks", x => x.id);
                    table.ForeignKey(
                        name: "fk_subtitle_tracks_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transcripts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "text", nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    word_timestamps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transcripts", x => x.id);
                    table.ForeignKey(
                        name: "fk_transcripts_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subtitle_track_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    speech_provider = table.Column<string>(type: "text", nullable: true),
                    speech_model = table.Column<string>(type: "text", nullable: true),
                    llm_provider = table.Column<string>(type: "text", nullable: true),
                    llm_model = table.Column<string>(type: "text", nullable: true),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_generations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_generations_prompt_versions_prompt_version_id",
                        column: x => x.prompt_version_id,
                        principalTable: "prompt_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_generations_subtitle_tracks_subtitle_track_id",
                        column: x => x.subtitle_track_id,
                        principalTable: "subtitle_tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_generations_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subtitle_cues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subtitle_track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    start_time_ms = table.Column<int>(type: "integer", nullable: false),
                    end_time_ms = table.Column<int>(type: "integer", nullable: false),
                    generated_text = table.Column<string>(type: "text", nullable: false),
                    edited_text = table.Column<string>(type: "text", nullable: true),
                    is_manually_edited = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subtitle_cues", x => x.id);
                    table.ForeignKey(
                        name: "fk_subtitle_cues_subtitle_tracks_subtitle_track_id",
                        column: x => x.subtitle_track_id,
                        principalTable: "subtitle_tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "words",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    start_time_ms = table.Column<int>(type: "integer", nullable: true),
                    end_time_ms = table.Column<int>(type: "integer", nullable: true),
                    is_highlighted_auto = table.Column<bool>(type: "boolean", nullable: false),
                    is_highlighted_manual_override = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_words", x => x.id);
                    table.ForeignKey(
                        name: "fk_words_subtitle_cues_cue_id",
                        column: x => x.cue_id,
                        principalTable: "subtitle_cues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_prompt_version_id",
                table: "ai_generations",
                column: "prompt_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_stage_prompt_version_id",
                table: "ai_generations",
                columns: new[] { "stage", "prompt_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_subtitle_track_id",
                table: "ai_generations",
                column: "subtitle_track_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_video_id_stage",
                table: "ai_generations",
                columns: new[] { "video_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exports_video_id",
                table: "exports",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_status_available_at_created_at",
                table: "processing_jobs",
                columns: new[] { "status", "available_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_video_id",
                table: "processing_jobs",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_versions_task",
                table: "prompt_versions",
                column: "task",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_versions_task_version",
                table: "prompt_versions",
                columns: new[] { "task", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subtitle_cues_subtitle_track_id_sequence_number",
                table: "subtitle_cues",
                columns: new[] { "subtitle_track_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subtitle_tracks_video_id_track_type",
                table: "subtitle_tracks",
                columns: new[] { "video_id", "track_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transcripts_video_id",
                table: "transcripts",
                column: "video_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_account_id",
                table: "users",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_videos_account_id",
                table: "videos",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_videos_status",
                table: "videos",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_videos_uploaded_by_user_id",
                table: "videos",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_words_cue_id_sequence_number",
                table: "words",
                columns: new[] { "cue_id", "sequence_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_generations");

            migrationBuilder.DropTable(
                name: "exports");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "transcripts");

            migrationBuilder.DropTable(
                name: "words");

            migrationBuilder.DropTable(
                name: "prompt_versions");

            migrationBuilder.DropTable(
                name: "subtitle_cues");

            migrationBuilder.DropTable(
                name: "subtitle_tracks");

            migrationBuilder.DropTable(
                name: "videos");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
