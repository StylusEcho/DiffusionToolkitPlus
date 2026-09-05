# Remote control protocol

Lets another application on the same computer drive Diffusion Toolkit — rate, tag, page through
results — while its own window has focus. Written for a Stream Deck plugin, but nothing about it is
Stream Deck specific.

Off by default. Turn it on in **Settings → General → Allow control from another application on this
computer**, and set the port there. Both settings live in `config.extended.json`.

## Connecting

Plain TCP. **Connect to `127.0.0.1`, not `localhost`.** The listener binds
`IPAddress.Loopback`, which is IPv4 only, and on Windows `localhost` usually resolves to `::1`
first — a client that uses the name will look like it is talking to a dead port.

Nothing outside this computer can reach it. Anything running *on* it can, so treat the port as
trusted-local only.

One JSON object per line, UTF-8, `\n` terminated, in both directions. Several clients may connect at
once; state is sent to all of them.

## Sending a command

```json
{"id": 1, "action": "rate", "value": 3}
```

`id` is optional and echoed back so replies can be matched to requests. `value` is required only by
the actions that say so below.

Every command gets exactly one reply:

```json
{"id": 1, "ok": true, "error": null}
{"id": 1, "ok": false, "error": "locked while reviewing"}
```

Errors are reported, never thrown away — a button that silently does nothing is worse than one that
says why. Expect `unknown action`, `missing action`, `invalid json`, `not ready` (the library is
still loading), `not available` (the command has no target right now), `locked while reviewing`,
`timed out`, and the validation messages for `rate` and `filter.type`.

## Receiving state

Sent unsolicited whenever it changes, and once when you connect, so a controller that starts late is
still correct:

```json
{"event": "state", "page": 4, "pages": 37, "results": 1832, "reviewing": true,
 "hasReviewSession": true, "autoAdvance": false, "fitToPreview": true,
 "actualSize": false, "hasFilter": false, "busy": false}
```

A burst of changes during a page load is debounced into one message.

## Actions

### Marking the selection

| Action | Value | |
|---|---|---|
| `rate` | `1`–`10` | Rates every selected image |
| `unrate` | | Clears the rating |
| `favorite` | | Toggles favourite |
| `nsfw` | | Toggles NSFW |
| `delete` | | Toggles marked for deletion |

Unavailable files are skipped with a toast rather than failing the whole call.

### Moving around

| Action | |
|---|---|
| `nav.next` / `nav.prev` | Next / previous image, rolling onto the next page at the edge |
| `page.next` / `page.prev` | Next / previous page |

### Views and filters

| Action | Value | |
|---|---|---|
| `view.folders` / `view.images` / `view.favorites` / `view.deleted` | | Switch library section |
| `quickalbum.open` | | Open the quick album as a view |
| `filter.type` | `"Image"` or `"Video"` | Toggle that media type in the filter |
| `filter.clear` | | Clear the query and filter |

All of these change which images are on screen, so **all of them are refused while a review is
running**, exactly as the equivalent controls are disabled in the window. They return
`locked while reviewing`.

### Everything else

| Action | |
|---|---|
| `quickalbum.toggle` | Add the selection to the quick album, or take it out |
| `review.toggle` | Start, leave, or resume a review |
| `info.toggle` | Show/hide the info overlay (full screen viewer only) |
| `zoom.fit` / `zoom.actual` | Fit to preview / actual size |
| `autoadvance.toggle` | Auto-advance after marking |
| `refresh` | Re-run the current search |
| `explorer.show` | Show the current image in Explorer |

`info.toggle` has no state in the pushed payload — it lives per-image rather than globally — so a
button for it should be stateless.

## Trying it without hardware

```
$ nc 127.0.0.1 9760
{"event":"state","page":1,"pages":12,...}
{"id":1,"action":"rate","value":3}
{"id":1,"ok":true,"error":null}
{"id":2,"action":"page.next"}
{"id":2,"ok":true,"error":null}
{"event":"state","page":2,"pages":12,...}
```
