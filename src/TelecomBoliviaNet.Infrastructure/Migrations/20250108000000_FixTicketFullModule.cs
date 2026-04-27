using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Aplica los cambios que debía haber aplicado 20250106000000_AddTicketFullModule
    /// pero que no se ejecutaron en la base de datos (la migración estaba registrada
    /// en __EFMigrationsHistory pero los objetos no existían).
    /// </summary>
    [Migration("20250108000000_FixTicketFullModule")]
    public partial class FixTicketFullModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Columnas faltantes en SupportTickets ──────────────────────────
            // Cada AddColumn usa IF NOT EXISTS lógicamente: si ya existe, EF lanzaría
            // error, pero como sabemos que NO existen, las creamos directamente.

            migrationBuilder.Sql(@"
                ALTER TABLE ""SupportTickets""
                    ADD COLUMN IF NOT EXISTS ""Subject""            character varying(200) NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS ""SupportGroup""       character varying(100),
                    ADD COLUMN IF NOT EXISTS ""FirstRespondedAt""   timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS ""ClosedAt""           timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS ""ResolutionMessage""  text,
                    ADD COLUMN IF NOT EXISTS ""RootCause""          text,
                    ADD COLUMN IF NOT EXISTS ""CsatScore""          integer,
                    ADD COLUMN IF NOT EXISTS ""CsatRespondedAt""    timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS ""SlaCompliant""       boolean,
                    ADD COLUMN IF NOT EXISTS ""SlaAlertSentAt""     timestamp with time zone;
            ");

            // ── SlaPlans ──────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SlaPlans"" (
                    ""Id""                   uuid                     NOT NULL,
                    ""Name""                 character varying(100)   NOT NULL,
                    ""Priority""             character varying(15)    NOT NULL,
                    ""FirstResponseMinutes"" integer                  NOT NULL,
                    ""ResolutionMinutes""    integer                  NOT NULL,
                    ""Schedule""             character varying(20)    NOT NULL,
                    ""IsActive""             boolean                  NOT NULL,
                    ""CreatedAt""            timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_SlaPlans"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SlaPlans_Priority"" ON ""SlaPlans"" (""Priority"");
            ");

            // Seed data (INSERT OR IGNORE via ON CONFLICT)
            migrationBuilder.Sql(@"
                INSERT INTO ""SlaPlans"" (""Id"",""Name"",""Priority"",""FirstResponseMinutes"",""ResolutionMinutes"",""Schedule"",""IsActive"",""CreatedAt"")
                VALUES
                    ('00000000-0000-0000-0002-000000000001','SLA Crítico','Critica',15, 240, 'Veinticuatro7',true,'2025-01-01 00:00:00+00'),
                    ('00000000-0000-0000-0002-000000000002','SLA Alto',   'Alta',   30, 480, 'Veinticuatro7',true,'2025-01-01 00:00:00+00'),
                    ('00000000-0000-0000-0002-000000000003','SLA Medio',  'Media',  120,1440,'Laboral',       true,'2025-01-01 00:00:00+00'),
                    ('00000000-0000-0000-0002-000000000004','SLA Bajo',   'Baja',   240,4320,'Laboral',       true,'2025-01-01 00:00:00+00')
                ON CONFLICT (""Id"") DO NOTHING;
            ");

            // ── TicketNotifications ───────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TicketNotifications"" (
                    ""Id""          uuid                     NOT NULL,
                    ""TicketId""    uuid                     NOT NULL,
                    ""Type""        character varying(30)    NOT NULL,
                    ""Status""      character varying(10)    NOT NULL,
                    ""Recipient""   character varying(200)   NOT NULL,
                    ""Message""     text                     NOT NULL,
                    ""ErrorDetail"" text,
                    ""SentAt""      timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_TicketNotifications"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_TicketNotifications_SupportTickets_TicketId""
                        FOREIGN KEY (""TicketId"") REFERENCES ""SupportTickets""(""Id"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_TicketNotifications_TicketId"" ON ""TicketNotifications""(""TicketId"");
            ");

            // ── TicketComments ────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TicketComments"" (
                    ""Id""        uuid                     NOT NULL,
                    ""TicketId""  uuid                     NOT NULL,
                    ""AuthorId""  uuid                     NOT NULL,
                    ""Type""      character varying(30)    NOT NULL,
                    ""Body""      text                     NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_TicketComments"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_TicketComments_SupportTickets_TicketId""
                        FOREIGN KEY (""TicketId"") REFERENCES ""SupportTickets""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_TicketComments_UserSystems_AuthorId""
                        FOREIGN KEY (""AuthorId"") REFERENCES ""UserSystems""(""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_TicketComments_TicketId"" ON ""TicketComments""(""TicketId"");
            ");

            // ── TicketWorkLogs ────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TicketWorkLogs"" (
                    ""Id""       uuid                     NOT NULL,
                    ""TicketId"" uuid                     NOT NULL,
                    ""UserId""   uuid                     NOT NULL,
                    ""Minutes""  integer                  NOT NULL,
                    ""Notes""    text,
                    ""LoggedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_TicketWorkLogs"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_TicketWorkLogs_SupportTickets_TicketId""
                        FOREIGN KEY (""TicketId"") REFERENCES ""SupportTickets""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_TicketWorkLogs_UserSystems_UserId""
                        FOREIGN KEY (""UserId"") REFERENCES ""UserSystems""(""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_TicketWorkLogs_TicketId"" ON ""TicketWorkLogs""(""TicketId"");
            ");

            // ── TicketVisits ──────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TicketVisits"" (
                    ""Id""              uuid                     NOT NULL,
                    ""TicketId""        uuid                     NOT NULL,
                    ""ScheduledAt""     timestamp with time zone NOT NULL,
                    ""TechnicianId""    uuid,
                    ""Observations""    text,
                    ""CreatedByUserId"" uuid                     NOT NULL,
                    ""CreatedAt""       timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_TicketVisits"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_TicketVisits_SupportTickets_TicketId""
                        FOREIGN KEY (""TicketId"") REFERENCES ""SupportTickets""(""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_TicketVisits_UserSystems_TechnicianId""
                        FOREIGN KEY (""TechnicianId"") REFERENCES ""UserSystems""(""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_TicketVisits_TicketId"" ON ""TicketVisits""(""TicketId"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""TicketComments"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""TicketWorkLogs"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""TicketVisits"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""TicketNotifications"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SlaPlans"";");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SupportTickets""
                    DROP COLUMN IF EXISTS ""Subject"",
                    DROP COLUMN IF EXISTS ""SupportGroup"",
                    DROP COLUMN IF EXISTS ""FirstRespondedAt"",
                    DROP COLUMN IF EXISTS ""ClosedAt"",
                    DROP COLUMN IF EXISTS ""ResolutionMessage"",
                    DROP COLUMN IF EXISTS ""RootCause"",
                    DROP COLUMN IF EXISTS ""CsatScore"",
                    DROP COLUMN IF EXISTS ""CsatRespondedAt"",
                    DROP COLUMN IF EXISTS ""SlaCompliant"",
                    DROP COLUMN IF EXISTS ""SlaAlertSentAt"";
            ");
        }
    }
}
