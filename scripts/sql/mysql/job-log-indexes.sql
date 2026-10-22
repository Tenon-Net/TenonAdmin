-- MySQL 8.0 / InnoDB：在目标数据库执行，补齐任务调度热路径索引（issue #85）。
-- 按前两列识别已有可见 B-tree 索引，兼容手工添加时使用了不同索引名的情况。
-- 同一数据库串行执行；可重复执行，不删除或重建已有索引。
SET @tenon_job_log_ddl = IF(
    EXISTS (
        SELECT 1
        FROM information_schema.STATISTICS AS first_col
        JOIN information_schema.STATISTICS AS second_col
          ON second_col.TABLE_SCHEMA = first_col.TABLE_SCHEMA
         AND second_col.TABLE_NAME = first_col.TABLE_NAME
         AND second_col.INDEX_NAME = first_col.INDEX_NAME
         AND second_col.SEQ_IN_INDEX = 2
        WHERE first_col.TABLE_SCHEMA = DATABASE()
          AND first_col.TABLE_NAME = 'sys_job_log'
          AND first_col.SEQ_IN_INDEX = 1
          AND first_col.COLUMN_NAME = 'JobId'
          AND second_col.COLUMN_NAME = 'EndTime'
          AND first_col.INDEX_TYPE = 'BTREE'
          AND first_col.IS_VISIBLE = 'YES'
    ),
    'DO 0',
    'ALTER TABLE `sys_job_log` ADD INDEX `idx_sys_job_log_job_end` (`JobId`, `EndTime`), ALGORITHM=INPLACE, LOCK=NONE'
);
PREPARE tenon_job_log_stmt FROM @tenon_job_log_ddl;
EXECUTE tenon_job_log_stmt;
DEALLOCATE PREPARE tenon_job_log_stmt;

SET @tenon_job_log_ddl = IF(
    EXISTS (
        SELECT 1
        FROM information_schema.STATISTICS AS first_col
        JOIN information_schema.STATISTICS AS second_col
          ON second_col.TABLE_SCHEMA = first_col.TABLE_SCHEMA
         AND second_col.TABLE_NAME = first_col.TABLE_NAME
         AND second_col.INDEX_NAME = first_col.INDEX_NAME
         AND second_col.SEQ_IN_INDEX = 2
        WHERE first_col.TABLE_SCHEMA = DATABASE()
          AND first_col.TABLE_NAME = 'sys_job_log'
          AND first_col.SEQ_IN_INDEX = 1
          AND first_col.COLUMN_NAME = 'EndTime'
          AND second_col.COLUMN_NAME = 'StartTime'
          AND first_col.INDEX_TYPE = 'BTREE'
          AND first_col.IS_VISIBLE = 'YES'
    ),
    'DO 0',
    'ALTER TABLE `sys_job_log` ADD INDEX `idx_sys_job_log_end_start` (`EndTime`, `StartTime`), ALGORITHM=INPLACE, LOCK=NONE'
);
PREPARE tenon_job_log_stmt FROM @tenon_job_log_ddl;
EXECUTE tenon_job_log_stmt;
DEALLOCATE PREPARE tenon_job_log_stmt;

ANALYZE TABLE `sys_job_log`;
