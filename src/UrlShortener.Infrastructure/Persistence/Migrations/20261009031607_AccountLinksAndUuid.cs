using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Persistence.Migrations;

public partial class AccountLinksAndUuid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE urls DROP CONSTRAINT "FK_urls_users_user_id";
            DROP INDEX "IX_urls_user_id";
            ALTER TABLE users ADD COLUMN new_id uuid NOT NULL DEFAULT gen_random_uuid();
            ALTER TABLE urls ADD COLUMN new_user_id uuid;
            UPDATE urls AS url SET new_user_id = owner.new_id
              FROM users AS owner WHERE url.user_id = owner.id;
            ALTER TABLE users DROP CONSTRAINT "PK_users";
            ALTER TABLE urls DROP COLUMN user_id;
            ALTER TABLE users DROP COLUMN id;
            ALTER TABLE users RENAME COLUMN new_id TO id;
            ALTER TABLE users ALTER COLUMN id DROP DEFAULT;
            ALTER TABLE urls RENAME COLUMN new_user_id TO user_id;
            ALTER TABLE users ADD CONSTRAINT "PK_users" PRIMARY KEY (id);
            CREATE INDEX "IX_urls_user_id" ON urls (user_id);
            ALTER TABLE urls ADD CONSTRAINT "FK_urls_users_user_id"
              FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT;
            ALTER TABLE users ADD COLUMN avatar_url varchar(2048);
            ALTER TABLE urls ADD COLUMN click_count bigint NOT NULL DEFAULT 0;
            CREATE TABLE reserved_short_codes (code varchar(16) CONSTRAINT "PK_reserved_short_codes" PRIMARY KEY);
            INSERT INTO reserved_short_codes (code) SELECT short_code FROM urls;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE urls DROP CONSTRAINT "FK_urls_users_user_id";
            DROP INDEX "IX_urls_user_id";
            ALTER TABLE users ADD COLUMN old_id bigint;
            CREATE SEQUENCE users_rollback_id_seq;
            UPDATE users SET old_id = nextval('users_rollback_id_seq');
            ALTER TABLE users ALTER COLUMN old_id SET NOT NULL;
            ALTER TABLE urls ADD COLUMN old_user_id bigint;
            UPDATE urls AS url SET old_user_id = owner.old_id
              FROM users AS owner WHERE url.user_id = owner.id;
            ALTER TABLE users DROP CONSTRAINT "PK_users";
            ALTER TABLE urls DROP COLUMN user_id;
            ALTER TABLE users DROP COLUMN id;
            ALTER TABLE users RENAME COLUMN old_id TO id;
            ALTER TABLE urls RENAME COLUMN old_user_id TO user_id;
            ALTER TABLE users ALTER COLUMN id SET DEFAULT nextval('users_rollback_id_seq');
            ALTER SEQUENCE users_rollback_id_seq OWNED BY users.id;
            ALTER TABLE users ADD CONSTRAINT "PK_users" PRIMARY KEY (id);
            CREATE INDEX "IX_urls_user_id" ON urls (user_id);
            ALTER TABLE urls ADD CONSTRAINT "FK_urls_users_user_id"
              FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT;
            ALTER TABLE users DROP COLUMN avatar_url;
            ALTER TABLE urls DROP COLUMN click_count;
            DROP TABLE reserved_short_codes;
            """);
    }
}
