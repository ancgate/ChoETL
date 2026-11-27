# Generates the pandas/pyarrow Parquet files used by PartialIssueTests (#256).
# Usage: python generate_pandas_dates.py <output-dir>   (requires pandas and pyarrow)
import sys, pandas as pd
out = sys.argv[1]
df = pd.DataFrame({"id": range(27), "date": pd.date_range(start='2022-11-05', end='2022-12-01', freq='d')})
df.to_parquet(f"{out}/dates_default.parquet")                         # pyarrow default: INT64 TIMESTAMP(ns or us)
df.to_parquet(f"{out}/dates_int96.parquet", use_deprecated_int96_timestamps=True)  # legacy Spark/Impala INT96
df.to_parquet(f"{out}/dates_ms.parquet", coerce_timestamps="ms", allow_truncated_timestamps=True)
df2 = df.copy(); df2["day"] = df2["date"].dt.date                       # DATE (INT32) logical type
df2.to_parquet(f"{out}/dates_date32.parquet")
import pyarrow.parquet as pq
for f in ["dates_default", "dates_int96", "dates_ms", "dates_date32"]:
    print(f, pq.read_schema(f"{out}/{f}.parquet").types, pq.ParquetFile(f"{out}/{f}.parquet").schema)
