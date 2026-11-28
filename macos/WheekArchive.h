#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>

// The macOS SDK ships libarchive's linker stub without its headers.
struct archive;
struct archive_entry;

#define ARCHIVE_OK 0
#define ARCHIVE_EOF 1

struct archive *archive_read_new(void);
int archive_read_support_format_zip(struct archive *);
int archive_read_open_memory(struct archive *, const void *, size_t);
int archive_read_next_header(struct archive *, struct archive_entry **);
ssize_t archive_read_data(struct archive *, void *, size_t);
int archive_read_free(struct archive *);
const char *archive_entry_pathname(struct archive_entry *);
int64_t archive_entry_size(struct archive_entry *);
