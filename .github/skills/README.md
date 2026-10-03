# Agent skills

Skills that coding agents (GitHub Copilot, Claude Code) load when they work on Relio.
[`AGENTS.md`](../../AGENTS.md) takes precedence when a skill conflicts with it.

| Skill | Source | License | Relio changes |
|---|---|---|---|
| `frontend-design` | [anthropics/skills](https://github.com/anthropics/skills/tree/main/skills/frontend-design) | Apache-2.0 (see its `LICENSE.txt`) | None |
| `csharp-async` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/csharp-async) | MIT | None |
| `dotnet-best-practices` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/dotnet-best-practices) | MIT | Relio namespaces, xUnit + AwesomeAssertions, no AI, no resource files, .NET 10 |
| `dotnet-timezone` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/dotnet-timezone) | MIT | None |
| `csharp-xunit` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/csharp-xunit) | MIT | AwesomeAssertions |
| `ef-core` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/ef-core) | MIT | SQL Server integration tests instead of InMemory/SQLite |
| `gdpr-compliant` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/gdpr-compliant) | MIT | None |
| `github-actions-hardening` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/github-actions-hardening) | MIT | None |
| `containerize-aspnetcore` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/containerize-aspnetcore) | MIT | Defaults to .NET 10 images |
| `create-architectural-decision-record` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/create-architectural-decision-record) | MIT | None |

## github/awesome-copilot license

    MIT License
    
    Copyright GitHub, Inc.
    
    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:
    
    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
    
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.