# Perinma

## Setup

### Prerequisites
- [mise](https://mise.jdx.dev/) - Development environment tool
- Google OAuth Client ID (for Google Calendar integration)

### Environment Variables

This project uses mise to manage environment variables. Set up your Google OAuth Client ID:

```bash
mise set -E local GOOGLE_CLIENT_ID=your-client-id-here.apps.googleusercontent.com
```

To get a Google OAuth Client ID:
1. Go to [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
2. Create a new OAuth 2.0 Client ID
3. Set the application type to "Desktop app"
4. Copy the Client ID

### Prepare build

Run `mise prepare-secrets` to generate the build time secrets file.

### Building

```bash
mise run build-linux
# or
dotnet build
```

mise will automatically inject the environment variables when you run commands in the project directory.

### Running

```bash
dotnet run --project src/perinma.csproj
```
