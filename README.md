
# Country Document Submission API

A backend application built using ASP.NET Core Web API to manage country-specific document submissions, reviewer decisions, document versioning, audit history, and country distribution.

The application implements a fixed two-step workflow:

1. **Country Decision:** A Country Editor uploads a document and submits it to an assigned Country Reviewer for approval or return.
2. **Country Distribution:** After approval, the Country Editor records the distribution completion and recipient details.

The application focuses on workflow correctness, role-based authorization, country-level access restrictions, immutable document snapshots, and auditability.

## Technology Stack

- .NET 8 LTS / ASP.NET Core Web API
- C#
- Entity Framework Core 8
- SQLite
- Swagger / OpenAPI
- xUnit
- EF Core InMemory for testing
- Local file storage for document snapshots

> Note: The target framework and package versions should match the actual project files.

## Prerequisites

- .NET 8 SDK
- Visual Studio 2022 or later
- Git (optional)

Verify the installed SDK:

```bash
dotnet --version
```

## Getting Started

### 1. Clone or download the repository

```bash
git clone <repository-url>
cd CountryDocumentSubmission
```

### 2. Restore dependencies

```bash
dotnet restore
```

### 3. Build the solution

```bash
dotnet build
```

### 4. Run the application

```bash
dotnet run --project src/CountryDocumentSubmission.Api
```

Open the Swagger URL printed in the console, normally:

```text
https://localhost:<port>/swagger
```

The SQLite database is created automatically using `EnsureCreated`, and sample users are seeded during application initialization.

## Running Automated Tests

Execute the tests from the solution root:

```bash
dotnet test
```

The test project uses xUnit and EF Core InMemory to validate workflow-related behavior.

## Local Demo Identities

For this assessment, authentication is intentionally simplified using a custom HTTP request header.

Every API request must include:

```text
X-User-Id: <user-id>
```

The API resolves the user from the seeded database. The client cannot directly specify or modify the user's role or country through the request payload.

### Sample Users

| User ID | Role | Country |
|---|---|---|
| `editor-in` | CountryEditor | IN |
| `reviewer-in` | CountryReviewer | IN |
| `editor-us` | CountryEditor | US |
| `reviewer-us` | CountryReviewer | US |

### Example Request

```bash
curl -k -H "X-User-Id: editor-in" \
https://localhost:<port>/api/users/reviewers
```

Replace `<port>` with the HTTPS port printed when the application starts.

In Swagger, use the **Authorize** option to provide the user ID for the `X-User-Id` header.

**Security note:** This header-based identity mechanism is intended only for local assessment demonstration. Production applications should use a validated JWT/OIDC authentication mechanism.

## API Endpoints

The following endpoints implement the document submission workflow.

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/users/reviewers` | Lists active reviewers available to the current editor's country |
| POST | `/api/submissions` | Creates a new draft submission |
| POST | `/api/submissions/{id}/document` | Uploads a document |
| POST | `/api/submissions/{id}/submit` | Submits the document for review |
| POST | `/api/submissions/{id}/approve` | Approves a pending submission |
| POST | `/api/submissions/{id}/return` | Returns a pending submission with comments |
| POST | `/api/submissions/{id}/distribute` | Records distribution completion |
| GET | `/api/submissions/{id}` | Retrieves submission details and snapshot history |
| GET | `/api/submissions/{id}/audit` | Retrieves the audit history |
| GET | `/api/submissions/{id}/snapshots/{snapshotId}/download` | Downloads a specific document snapshot |

### Document Upload

The document upload endpoint accepts a multipart form-data request with the field name:

```text
file
```

The maximum upload size in this demo is 10 MB.

### Example Request Payloads

Create submission:

```json
{
  "title": "Annual Country Report 2026"
}
```

Submit for review:

```json
{
  "reviewerId": "reviewer-in"
}
```

Return for corrections:

```json
{
  "comments": "Please correct the missing information in Section 2."
}
```

Record distribution:

```json
{
  "recipient": "Country Operations Team"
}
```

Use the exact request schemas exposed by Swagger if they differ from these examples.

## Workflow Demonstration

### Scenario 1: Successful Approval and Distribution

1. Authorize as `editor-in`.
2. Create a new submission.
3. Upload the country document.
4. Submit it for review, assigning `reviewer-in`.
5. Authorize as `reviewer-in`.
6. Approve the submission.
7. Authorize again as `editor-in`.
8. Record distribution completion with recipient details.
9. Verify that the submission status is `Distributed`.

### Scenario 2: Return, Revision and Resubmission

1. Authorize as `editor-in`.
2. Create a new submission and upload a document.
3. Submit it for review.
4. Authorize as `reviewer-in`.
5. Return the submission with correction comments.
6. Authorize again as `editor-in`.
7. Upload the revised document.
8. Resubmit the submission for review.
9. Verify that the original and revised document snapshots are preserved in the submission history.

## Submission States

The application uses the following workflow states:

| State | Description |
|---|---|
| Draft | Submission created by the Country Editor |
| PendingReview | Document submitted and awaiting reviewer action |
| Returned | Reviewer has requested corrections |
| Approved | Reviewer has approved the document |
| Distributed | Editor has recorded distribution completion |

## Authorization and Business Rules

- Only Country Editors can create submissions, upload documents, submit for review, and record distribution.
- An Editor can access only their own submissions within their country.
- A Reviewer can access only submissions assigned to them within their country.
- Reviewer assignment is validated against active reviewer users belonging to the same country.
- Only the assigned Reviewer can approve or return a submission in `PendingReview` status.
- A submission can be distributed only after approval.
- A returned submission can be revised and resubmitted by its Editor.
- Previous document snapshots are preserved and never overwritten.
- Approval records the exact `ApprovedSnapshotId` to identify the document version that was approved.
- Workflow actions are recorded in the audit history.

## Data Model

The application uses the following primary entities:

### AppUser
Stores local user identity, role, country code, and active status.

### Submission
Represents the workflow aggregate and stores the submission owner, country, assigned reviewer, current status, and approved snapshot reference.

### DocumentSnapshot
Stores immutable metadata for each uploaded document version, including the original filename, generated storage filename, version information, and SHA-256 hash.

### AuditEntry
Records workflow activities, including the actor, action, relevant details, and UTC timestamp.

### Document Storage

Uploaded document bytes are stored under:

```text
App_Data/uploads/
```

The database stores a generated storage filename rather than trusting a client-supplied file path.

## Assumptions and Engineering Trade-offs

- Each submission has one Editor owner and one assigned Reviewer at a time.
- Country information is obtained from the authenticated user's database record, never from client-supplied country values.
- A Reviewer can approve or return a submission only when its status is `PendingReview`.
- Returning a submission requires comments.
- Distribution requires recipient details and represents a recorded workflow completion, not actual external document delivery.
- Files are limited to 10 MB in this demonstration.
- Each document upload creates a separate snapshot record and file. Existing snapshots are not overwritten.
- SHA-256 hashes are recorded to support document integrity verification. A hash alone does not provide immutable or tamper-proof storage.

### Production Improvements

The following improvements would be considered before deploying this solution in a production environment:

- Replace the demo header-based identity mechanism with JWT/OIDC authentication and claims-based authorization.
- Use HTTPS, secure secrets management, and rate limiting.
- Add file extension and content-type validation, malware scanning, and encrypted object storage.
- Use EF Core migrations instead of `EnsureCreated` for database schema management.
- Implement database-level restrictions and tamper-evident audit logging.
- Add optimistic concurrency control to prevent conflicting reviewer decisions.
- Add transaction handling and compensating cleanup for failures between file storage and database persistence.
- Introduce centralized exception handling and structured application logging.
- Expand automated integration tests to cover complete API workflows, authorization boundaries, and failure scenarios.

## Future Enhancements

Potential future enhancements include:

- Integration with cloud-based document storage.
- Notifications for submission, approval, and return events.
- Advanced audit reporting and monitoring.
- AI-assisted preparation of decision documents from an existing PDF/DOCX repository, with human review before submission.
