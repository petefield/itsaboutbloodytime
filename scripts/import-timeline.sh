#!/usr/bin/env bash
set -euo pipefail

readonly DEFAULT_API_BASE_URL="https://white-tree-0af7bee10.7.azurestaticapps.net/api"
readonly MAXIMUM_IMAGE_SIZE=5242880
readonly DEFAULT_IMAGE_DOWNLOAD_USER_AGENT="HistoricalTimelineImporter/1.0 (https://github.com/petefield/itsaboutbloodytime; bot)"

progress() {
    printf '\n==> %s\n' "$1"
}

read_error_response() {
    local response_file=$1

    if [[ ! -s "$response_file" ]]; then
        printf 'No response body was returned.'
    elif ! jq --compact-output . "$response_file" 2>/dev/null; then
        tr '\n' ' ' <"$response_file"
    fi
}

usage() {
    cat <<'EOF'
Usage: import-timeline.sh CSV_PATH [TIMELINE_TITLE] [TIMELINE_DESCRIPTION]

Creates a timeline and imports every CSV row as an event. The CSV must contain:
  start_date, end_date, title, summary, full_description

The optional image_url column may contain an HTTP(S) URL, a file:// URL, or a
path relative to the CSV file. Each image is uploaded with its event.
Downloaded HTTP(S) images are retained beside the CSV using the CSV filename
and event number.
Images larger than 5 MB are resized and compressed to fit the upload limit.
Unavailable or unsupported images are skipped while their event is imported.

Set API_BASE_URL to target a different API endpoint. It defaults to the
deployed API:
  https://white-tree-0af7bee10.7.azurestaticapps.net/api

Set IMAGE_DOWNLOAD_USER_AGENT to override the identifying User-Agent sent
when retrieving images. The default identifies this importer to Wikimedia.
EOF
}

if [[ $# -eq 0 || "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 0
fi

if [[ $# -gt 3 ]]; then
    usage >&2
    exit 1
fi

csv_path=$1
if [[ ! -f "$csv_path" ]]; then
    printf 'CSV file not found: %s\n' "$csv_path" >&2
    exit 1
fi

progress "Checking required tools"
for command in curl file jq python3; do
    if ! command -v "$command" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$command" >&2
        exit 1
    fi
done

csv_path=$(cd "$(dirname "$csv_path")" && pwd)/$(basename "$csv_path")
csv_directory=$(dirname "$csv_path")
csv_filename=$(basename "$csv_path")
filename_stem=${csv_filename%.csv}
filename_stem=${filename_stem%_significant_events}
default_title=${filename_stem//_/ }
default_title=${default_title//-/ }
timeline_title=${2:-"$default_title"}
timeline_description=${3:-"Imported from ${csv_filename}."}
api_base_url=${API_BASE_URL:-"$DEFAULT_API_BASE_URL"}
api_base_url=${api_base_url%/}
image_download_user_agent=${IMAGE_DOWNLOAD_USER_AGENT:-"$DEFAULT_IMAGE_DOWNLOAD_USER_AGENT"}

if [[ ${#timeline_title} -gt 200 || ${#timeline_description} -gt 500 ]]; then
    printf 'Timeline title must be 200 characters or fewer and description 500 characters or fewer.\n' >&2
    exit 1
fi

temporary_directory=$(mktemp -d "${TMPDIR:-/tmp}/timeline-import.XXXXXX")
trap 'rm -rf -- "$temporary_directory"' EXIT

records_file="$temporary_directory/events.jsonl"
progress "Reading and validating CSV: $csv_filename"
python3 - "$csv_path" >"$records_file" <<'PYTHON'
import csv
import json
import sys
from datetime import date

csv_path = sys.argv[1]
required_columns = {"start_date", "end_date", "title", "summary", "full_description"}

with open(csv_path, encoding="utf-8-sig", newline="") as source:
    reader = csv.DictReader(source)
    columns = set(reader.fieldnames or [])
    missing_columns = required_columns - columns
    if missing_columns:
        raise SystemExit(
            "CSV is missing required column(s): " + ", ".join(sorted(missing_columns))
        )

    for row_number, row in enumerate(reader, start=2):
        event = {
            "title": (row["title"] or "").strip(),
            "summary": (row["summary"] or "").strip(),
            "description": (row["full_description"] or "").strip(),
            "image_url": (row.get("image_url") or "").strip(),
        }
        for field in ("title", "summary", "description"):
            if not event[field]:
                raise SystemExit(f"Row {row_number} has an empty {field}.")

        try:
            event["start_date"] = date.fromisoformat(row["start_date"].strip()[:10]).isoformat()
            event["end_date"] = date.fromisoformat(row["end_date"].strip()[:10]).isoformat()
        except ValueError as error:
            raise SystemExit(f"Row {row_number} has an invalid date: {error}") from error

        if event["end_date"] < event["start_date"]:
            raise SystemExit(f"Row {row_number} ends before it starts.")

        print(json.dumps(event, ensure_ascii=False))
PYTHON

mapfile -t event_records <"$records_file"

if [[ ${#event_records[@]} -eq 0 ]]; then
    printf 'CSV contains no events: %s\n' "$csv_path" >&2
    exit 1
fi

declare -a image_paths=()
declare -a image_mime_types=()
declare -a image_extensions=()

progress "Preparing images for ${#event_records[@]} events"
for index in "${!event_records[@]}"; do
    title=$(jq -r '.title' <<<"${event_records[$index]}")
    image_url=$(jq -r '.image_url' <<<"${event_records[$index]}")
    printf '  [%d/%d] %s\n' "$((index + 1))" "${#event_records[@]}" "$title"
    if [[ -z "$image_url" ]]; then
        printf '    No image supplied; importing event without one.\n'
        image_paths[$index]=''
        image_mime_types[$index]=''
        continue
    fi

    if [[ "$image_url" == http://* || "$image_url" == https://* ]]; then
        image_path="$csv_directory/${filename_stem}-image-$((index + 1))"
        printf '    Downloading image...\n'
        if ! curl --fail --location --connect-timeout 10 --max-time 30 --silent --show-error \
            --user-agent "$image_download_user_agent" \
            --output "$image_path" "$image_url"; then
            printf 'Unable to retrieve image for event %d; importing without it.\n' "$((index + 1))" >&2
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
        fi
    else
        image_path="$temporary_directory/image-$index"
        printf '    Reading local image...\n'
        source_path=${image_url#file://}
        if [[ "$source_path" != /* ]]; then
            source_path="$csv_directory/$source_path"
        fi
        if [[ ! -f "$source_path" ]]; then
            printf 'Image file not found for event %d; importing without it: %s\n' "$((index + 1))" "$source_path" >&2
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
        fi
        if ! cp -- "$source_path" "$image_path"; then
            printf 'Unable to copy image for event %d; importing without it.\n' "$((index + 1))" >&2
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
        fi
    fi

    if [[ ! -s "$image_path" ]]; then
        printf 'Image for event %d is empty; importing without it.\n' "$((index + 1))" >&2
        image_paths[$index]=''
        image_mime_types[$index]=''
        continue
    fi

    image_size=$(wc -c <"$image_path")
    if [[ $image_size -gt $MAXIMUM_IMAGE_SIZE ]]; then
        if ! command -v magick >/dev/null 2>&1; then
            printf 'Image for event %d exceeds 5 MB and ImageMagick is unavailable; importing without it.\n' "$((index + 1))" >&2
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
        fi

        compressed_image_path="$temporary_directory/image-$index.jpg"
        compressed=false
        compression_failed=false

        printf 'Reducing image for event %d to fit the 5 MB upload limit.\n' "$((index + 1))"
        for maximum_dimension in 2560 2048 1600 1280 960; do
            for quality in 85 75 65 55; do
                if ! magick "${image_path}[0]" \
                    -auto-orient \
                    -strip \
                    -resize "${maximum_dimension}x${maximum_dimension}>" \
                    -interlace Plane \
                    -quality "$quality" \
                    "$compressed_image_path"; then
                    compression_failed=true
                    break 2
                fi

                if [[ $(wc -c <"$compressed_image_path") -le $MAXIMUM_IMAGE_SIZE ]]; then
                    mv -- "$compressed_image_path" "$image_path"
                    compressed=true
                    break 2
                fi
            done
        done

        if [[ "$compressed" != true ]]; then
            if [[ "$compression_failed" == true ]]; then
                printf 'Unable to reduce image for event %d; importing without it.\n' "$((index + 1))" >&2
            else
                printf 'Image for event %d could not be reduced below 5 MB; importing without it.\n' "$((index + 1))" >&2
            fi
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
        fi
    fi

    if ! mime_type=$(file --brief --mime-type "$image_path"); then
        printf 'Unable to identify image type for event %d; importing without it.\n' "$((index + 1))" >&2
        image_paths[$index]=''
        image_mime_types[$index]=''
        continue
    fi
    case "$mime_type" in
        image/jpeg) image_extension=jpg ;;
        image/png) image_extension=png ;;
        image/gif) image_extension=gif ;;
        image/webp) image_extension=webp ;;
        *)
            printf 'Unsupported image type for event %d: %s; importing without it.\n' "$((index + 1))" "$mime_type" >&2
            image_paths[$index]=''
            image_mime_types[$index]=''
            continue
            ;;
    esac

    image_paths[$index]=$image_path
    image_mime_types[$index]=$mime_type
    image_extensions[$index]=$image_extension
    if [[ "$image_url" == http://* || "$image_url" == https://* ]]; then
        downloaded_image_path="${image_path}.${image_extension}"
        mv --force "$image_path" "$downloaded_image_path"
        image_paths[$index]=$downloaded_image_path
        printf '    Saved image: %s\n' "$(basename "$downloaded_image_path")"
    fi
    printf '    Image ready.\n'
done

progress "Creating timeline: $timeline_title"
timeline_response=$(curl --fail-with-body --silent --show-error \
    --request POST "$api_base_url/timelines" \
    --form-string "title=$timeline_title" \
    --form-string "description=$timeline_description")
timeline_id=$(jq --exit-status --raw-output '.id' <<<"$timeline_response")
printf 'Timeline created: %s\n' "$timeline_id"

progress "Uploading ${#event_records[@]} events"
for index in "${!event_records[@]}"; do
    event_record=${event_records[$index]}
    title=$(jq -r '.title' <<<"$event_record")
    summary=$(jq -r '.summary' <<<"$event_record")
    description=$(jq -r '.description' <<<"$event_record")
    start_date=$(jq -r '.start_date' <<<"$event_record")
    end_date=$(jq -r '.end_date' <<<"$event_record")

    curl_arguments=(
        --fail-with-body
        --silent
        --show-error
        --request POST
        --form-string "title=$title"
        --form-string "summary=$summary"
        --form-string "description=$description"
        --form-string "startDate=$start_date"
        --form-string "endDate=$end_date"
    )
    event_url="$api_base_url/timelines/$timeline_id/historical-events"
    response_file="$temporary_directory/event-response-$index.json"
    event_uploaded=false
    printf '  [%d/%d] Uploading: %s\n' "$((index + 1))" "${#event_records[@]}" "$title"
    if [[ -n "${image_paths[$index]}" ]]; then
        image_curl_arguments=(
            "${curl_arguments[@]}"
            --form "image=@${image_paths[$index]};filename=event-image.${image_extensions[$index]};type=${image_mime_types[$index]}"
        )
        if response_status=$(curl "${image_curl_arguments[@]}" "$event_url" --output "$response_file" --write-out '%{http_code}'); then
            event_uploaded=true
        elif [[ "$response_status" == "400" ]]; then
            printf '    Image was rejected by the API; retrying event without it.\n' >&2
        else
            error_response=$(read_error_response "$response_file")
            printf 'Unable to upload event %d (HTTP %s): %s\n' "$((index + 1))" "$response_status" "$error_response" >&2
            exit 1
        fi
    fi

    if [[ "$event_uploaded" != true ]]; then
        if ! response_status=$(curl "${curl_arguments[@]}" "$event_url" --output "$response_file" --write-out '%{http_code}'); then
            error_response=$(read_error_response "$response_file")
            printf 'Unable to upload event %d (HTTP %s): %s\n' "$((index + 1))" "$response_status" "$error_response" >&2
            exit 1
        fi
    fi
done

printf '\nImported %d events into timeline %s.\n' "${#event_records[@]}" "$timeline_id"
printf 'Open the editor: %s/timelines/%s\n' "${api_base_url%/api}" "$timeline_id"
