# Taskify

Tablica Kanban w .NET 10 i Aspire: trzy API, aplikacja Blazor Server i PostgreSQL. Specyfikacja,
plan i lista zadań są w [specs/001-taskify-kanban-board/](specs/001-taskify-kanban-board/), a
zwięzły opis projektu dla agentów AI w [CLAUDE.md](CLAUDE.md).

## Build i testy

```powershell
./scripts/verify.ps1                                   # build (-warnaserror) + testy unit i bUnit
./scripts/verify.ps1 -Tests integration -Class '*CommentsContractTests'
./scripts/verify.ps1 -Tests all                        # to samo, co uruchamia CI
```

Testy integracyjne i E2E uruchamiają AppHost, więc potrzebują środowiska kontenerów (Docker albo
Podman). Gdy Dockera nie ma w PATH, `verify.ps1` sam ustawia `ASPIRE_CONTAINER_RUNTIME=podman` i
uruchamia maszynę Podmana.

## Implementacja z agentami AI: orchestrator → worker

Pierwsze przejście `/speckit-implement` przepełniło okno kontekstu i wyczerpało limit tokenów.
Kod, output buildów i stack trace'y gromadziły się w jednej sesji. Ukończona część jest w commicie
„Implementacja cześć 1”. Pozostałe zadania realizujemy we wzorcu orchestrator → worker.

### Elementy

| Plik | Rola |
| --- | --- |
| [scripts/verify.ps1](scripts/verify.ps1) | Build i testy z bardzo krótkim outputem: tylko podsumowanie oraz nazwy padających testów z 1–3 liniami błędu. Wymusza angielski output, żeby wzorce działały mimo polskiej lokalizacji. Sam uruchamia Podmana. |
| [CLAUDE.md](CLAUDE.md) | Brief ładowany do każdej sesji i każdego subagenta: układ projektów, komendy, twarde reguły, znane problemy. Agent nie musi czytać w całości spec/plan/research (ok. 1000 linii). |
| [.claude/agents/speckit-worker.md](.claude/agents/speckit-worker.md) | Worker (model Sonnet). Czyta z `tasks.md` tylko swoje taski, dokańcza istniejące pliki zamiast pisać je od nowa i weryfikuje zmiany przez `verify.ps1`. Nie commituje i nie odhacza tasków. Zwraca raport do ok. 25 linii. |
| [.claude/skills/speckit-implement-orchestrated/SKILL.md](.claude/skills/speckit-implement-orchestrated/SKILL.md) | Orkiestrator. Grupuje taski w jednostki po 3–8 i wysyła je do workerów, a potem **sam ponownie uruchamia testy**. Odhacza `[X]` i commituje po każdej jednostce, która przeszła. Równolegle puszcza tylko jednostki `[P]`, które nie dotykają wspólnych plików. |

Wygenerowany przez spec-kit `.claude/skills/speckit-implement/SKILL.md` zostaje bez zmian, bo
aktualizacja spec-kit by go nadpisała.

### Użycie

```text
/speckit-implement-orchestrated US4      # jedna historia użytkownika
/speckit-implement-orchestrated next     # wznowienie od pierwszego otwartego taska
```

Cały stan pracy jest w `tasks.md` i historii gita, więc przerwany przebieg można bezpiecznie
wznowić w nowej sesji. Przed startem skill wymaga czystego `git status`.

### Uwagi

- Subagenty rozwiązują problem przepełnionego okna kontekstu, a nie limitu tokenów. Każdy worker
  startuje od zera, więc łączne zużycie tokenów rośnie. Ograniczają je Sonnet w workerach,
  zwięzły output `verify.ps1` i jednostki obejmujące kilka tasków naraz.
- Orkiestrator nie wierzy raportom workerów na słowo: zadanie zostaje odhaczone dopiero wtedy,
  gdy testy przechodzą w jego własnym przebiegu `verify.ps1`.

## Stan na 2026-10-05

- Build zielony, 0 ostrzeżeń. Testy jednostkowe: 292/292, bUnit: 63/63, integracyjne: 154/163.
- Odhaczonych jest 99 z 151 zadań. Otwarte są US4 (T100–T109), US5 (T110–T126), US6 (T127–T137)
  oraz Polish (T138–T151).
- Pliki T100–T107 (komentarze, US4) już istnieją, a 24 z 27 testów kontraktowych komentarzy
  przechodzi. Zadania nie są jednak odhaczone, bo poprzednia sesja urwała się przed aktualizacją
  `tasks.md`. Worker ma je zweryfikować i dokończyć.
- **Znany problem:** `MoveContractTests` i `CreateEditContractTests` są niestabilne. Przy każdym
  przebiegu padają inne testy, w jednym przypadku z `429`. Najpewniej wszystkie testy dzielą jednego
  seedowanego użytkownika i trafiają w limit 60 zapisów na minutę (SC-010). Możliwe poprawki to
  osobny użytkownik na klasę testów albo wyższy limit w konfiguracji testowej. Do tego czasu
  padającą klasę trzeba uruchomić osobno, zanim uzna się porażkę za regresję.
