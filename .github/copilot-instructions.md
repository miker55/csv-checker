# Copilot Instructions

## General Guidelines
- Focus only on technical upgrade work.
- Do not perform any Git operations (branching, committing, stashing, merging); handle all Git operations manually.

## Code Style

### Method Parameters
When a method has more than 1 parameter, format them as follows:
- Each parameter on its own line
- Comma at the beginning of each line (except the first parameter)
- Proper indentation

Example:
```csharp
public async Task TrackAsync(
    string eventType
    , int? rowCount = null
    , int? columnCount = null
    , long? fileSizeBytes = null
    , CancellationToken ct = default
)
```