# DotBoxD docs site

The documentation site at <https://dotboxd.kamsker.at/>, built with
[Astro Starlight](https://starlight.astro.build/). Conceptual docs, tutorials, examples, and
reference pages live in `src/content/docs/`; the .NET API reference is generated into
`src/content/docs/api/` (gitignored) by DocFX.

## Local development

```bash
cd docs-site
npm install
npm run dev            # http://localhost:4321
```

Every page needs `title` (and ideally `description`) frontmatter. Internal links are written as
root-relative routes with trailing slashes (e.g. `/concepts/kernels/`), not `.md` file paths.

## Generating the API reference locally (optional)

The site builds without the API section when it hasn't been generated. To include it:

```bash
# From the repo root — builds the library projects and emits markdown:
dotnet tool restore
dotnet docfx metadata docfx.json

# Add frontmatter/slugs, rewrite links, build the API sidebar:
cd docs-site
npm run postprocess-api
```

## Production build

```bash
npm run build          # output in dist/
npm run preview        # serve the production build locally
```

CI (`.github/workflows/docs.yml`) runs the DocFX metadata step, the post-processing script, and
`astro build`, then publishes `dist/` to the `gh-pages` branch. The `gh-pages` branch also hosts
BenchmarkDotNet history under `dev/bench/` — the deploy step must keep using `clean-exclude` for
that path, and Pages must stay on "Deploy from a branch".
