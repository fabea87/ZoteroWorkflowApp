# Zotero AI Research Studio (ZoteroWorkflowApp)

A modern C# (.NET 8 WPF) desktop research workstation integrating **Zotero**, **ZotSeek** semantic search, **PdfPig / anydoc** Markdown full-text extraction, **OpenAlex** bibliometrics, **VOSviewer-style Co-occurrence & Topic Modeling**, **Connected Papers / ResearchRabbit** interactive knowledge graphs, and **WYSIWYG Literature Review** generation.

## Key Features

1. **ZotSeek Semantic Search & Vector Snowballing**
   - Connects to Zotero's local ZotSeek HTTP endpoint (`http://localhost:23119/zotseek`).
   - Supports `Hybrid`, `Semantic`, and `Keyword` search modes (`Papers` or `Passages` granularity) plus 1-click **Vector Snowballing (`/zotseek/similar`)** to expand seed papers.
   - Lock-free SQLite + WAL snapshot reader resolves Zotero linked PDFs (`baseAttachmentPath`), DOIs, journals, and abstracts without locking Zotero.

2. **Markdown-First Full-Text Pipeline (`PdfPig` + `anydoc`)**
   - Native C# `UglyToad.PdfPig` with `RecursiveXYCut` multi-column academic page segmentation (`~100–400ms` per paper) + `@firecrawl/anydoc` CLI support.
   - Embeds `[Page N]` markers and Obsidian YAML frontmatter into every extracted `.md` note.

3. **Concurrent Knowledge Graph, Text Mining & OpenAlex Bibliometrics**
   - **Concept Knowledge Graph**: Interactive `vis-network` graph (ForceAtlas2 & Hierarchical layouts), category filtering, and evidence inspector.
   - **Co-occurrence & Topic Modeling**: Extracts high-TF-IDF academic unigrams/bigrams, PMI collocation networks, and topic clusters.
   - **OpenAlex Scientometrics**: Fetches global citation counts, FWCI, bibliographic coupling, and discovers **Co-Cited Seminal & Missing Papers**.

4. **WYSIWYG Literature Review & Page-Level Citation Grounding**
   - 5 switchable academic themes (`Warm Paper Ink-Blue`, `Obsidian Vault`, `APA Print`, `GitHub Clean`, `Sage Eye-Care`).
   - Clickable `📄 p.N ↗` badges (`zotero://open-pdf/library/items/{Key}?page={N}`) jump directly to the exact PDF page in Zotero.
   - Dedicated **Page-Grounded Corpus Q&A** workspace for targeted multi-paper inquiries.

## Configuration

User settings and customizable prompts are automatically saved to:
`C:\Users\<Username>\zotero-workflow-settings.json`
