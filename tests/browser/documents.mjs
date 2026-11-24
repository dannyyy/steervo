import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

const DESKTOP_VIEWPORT = { width: 1440, height: 1050 };
const PHONE_VIEWPORT = { width: 390, height: 844 };
const EXPIRED_SESSION_TIMEOUT_MS = 10_000;
const UPLOAD_REQUESTS = "**/documents/upload*";
const PDF_MIME_TYPE = "application/pdf";
const FIRST_VERSION_BYTES = Buffer.from("%PDF-1.4\nDocument browser fixture: first version\n%%EOF");
const SECOND_VERSION_BYTES = Buffer.from(
  "%PDF-1.4\nDocument browser fixture: second version\n%%EOF",
);

/**
 * Covers documents attached to an offer in an evaluation of its own: upload, file history,
 * sharing, export/import and deletion. The steps build on each other and must run in this order.
 */
export async function checkDocuments({
  page,
  reader,
  readerEmail,
  origin,
  output,
  post,
  browserRequest,
  record,
  restart,
}) {
  const originalPath = new URL(page.url()).pathname;
  const context = {
    page,
    reader,
    readerEmail,
    origin,
    output,
    post,
    browserRequest,
    record,
    restart,
  };

  Object.assign(context, await createEvaluationWithOffer(context));
  Object.assign(context, await checkFirstUploadAndDownload(context));
  await checkFileHistorySurvivesRestart(context);
  await checkReaderMayOnlyDownload(context);
  await checkConcurrentUploadConflict(context);
  await checkRevokedAccess(context);
  await checkPhoneLayout(context);
  Object.assign(context, await exportAndImportEvaluation(context));
  await checkDeletionLeavesImportedCopy(context);
  await checkExpiredSessionKeepsUpload(context);

  await page.goto(`${origin}${originalPath}`);
}

// ---------------------------------------------------------------------------------------------
// Shared interactions
// ---------------------------------------------------------------------------------------------

function pdfFile(name, buffer) {
  return { name, mimeType: PDF_MIME_TYPE, buffer };
}

function documentName(target) {
  return target.getByLabel("Name des Dokuments");
}

function fileInput(target) {
  return target.getByLabel("Datei", { exact: true });
}

function fileVersionNote(target) {
  return target.getByLabel("Notiz zur Dateiversion", { exact: false });
}

function uploadButton(target) {
  return target.getByRole("button", { name: "Datei hochladen", exact: true });
}

function newFileVersionLink(page) {
  return page.getByRole("link", { name: "Neue Dateiversion", exact: true });
}

function deleteDocumentButton(page) {
  return page.getByRole("button", { name: "Dokument löschen", exact: true });
}

function selectedUploadFileName(target) {
  return target.locator("#upload").evaluate((input) => input.files[0].name);
}

/** Starts listening for the response to a form post back to the page that is currently open. */
function waitForPostToCurrentPage(page) {
  return page.waitForResponse(
    (response) => response.request().method() === "POST" && response.url() === page.url(),
  );
}

function fitsViewportWidth(page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
}

async function downloadedText(browserRequest, target, path) {
  return (await browserRequest(target, path)).text();
}

/** Opens the evaluation on the owner's page and returns its current version. */
async function evaluationVersion({ page, origin, casePath }) {
  await page.goto(`${origin}${casePath}`);
  return page.locator("#evaluation-workspace").getAttribute("data-version");
}

function shareEvaluation({ page, post, casePath, readerEmail }, access, version) {
  return post(page, casePath, { action: "share", email: readerEmail, access, version });
}

/**
 * Posts an upload directly, bypassing the form, and returns the HTTP status. The anti-forgery
 * token of the open page is sent along unless the caller leaves it out on purpose.
 */
async function postUploadDirectly(target, path, fields, includeToken = true) {
  const token = includeToken
    ? await target.locator('input[name="__RequestVerificationToken"]').first().inputValue()
    : null;

  return target.evaluate(
    async ({ path, fields, token }) => {
      const form = new FormData();
      for (const [key, value] of Object.entries(fields)) {
        form.append(key, value);
      }
      if (token) {
        form.append("__RequestVerificationToken", token);
      }
      form.append("upload", new Blob(["unauthorized"], { type: "application/pdf" }), "file.pdf");
      return (await fetch(path, { method: "POST", body: form, redirect: "manual" })).status;
    },
    { path, fields, token },
  );
}

// ---------------------------------------------------------------------------------------------
// Setup: an evaluation with one car and one offer
// ---------------------------------------------------------------------------------------------

async function createEvaluationWithOffer({ page, origin, post }) {
  await page.goto(origin);
  await page.locator('[data-dialog-open="new-evaluation"]').first().click();
  const creation = page.getByRole("dialog", { name: "Neue Evaluation", exact: true });
  await creation.getByLabel("Name der Evaluation").fill("Dokumentenprüfung");
  await page.locator("#new-region-country").selectOption("CH");
  await page.locator("#new-region-currency").fill("EUR");
  await creation.getByRole("button", { name: "Evaluation anlegen", exact: false }).click();
  await page.waitForURL(/\/evaluations\//);
  const casePath = new URL(page.url()).pathname;

  await page.goto(`${origin}${casePath}/candidates`);
  const version = await page.locator("#candidate-workspace").getAttribute("data-version");
  assert.equal(
    (
      await post(page, `${casePath}/candidates`, {
        action: "add-candidate",
        "Input.Model": "Dokumentenauto",
        version,
      })
    ).status(),
    0,
  );

  await page.goto(`${origin}${casePath}/offers/edit`);
  await page
    .locator("#offer-candidate")
    .selectOption(await page.locator("#offer-candidate option").nth(1).getAttribute("value"));
  assert.equal(await page.locator("#offer-currency").inputValue(), "EUR");
  await page.locator("#offer-Name").fill("Dokumentenofferte");
  await page.getByRole("button", { name: /Offerte anlegen/ }).click();
  await page.locator("#offer-workspace").waitFor();
  const offerPath = new URL(page.url()).pathname;

  return { casePath, offerPath };
}

// ---------------------------------------------------------------------------------------------
// First upload, failed attempts and download
// ---------------------------------------------------------------------------------------------

async function checkFirstUploadAndDownload({ page, record, casePath }) {
  await page.getByRole("link", { name: "Dokument hinzufügen", exact: true }).click();
  await documentName(page).fill("Vertragsunterlagen");
  await fileInput(page).setInputFiles(pdfFile("vertrag.pdf", FIRST_VERSION_BYTES));
  await fileVersionNote(page).fill("Erster Stand");

  // A dropped connection keeps the selected file.
  await page.route(UPLOAD_REQUESTS, (route) =>
    route.request().method() === "POST" ? route.abort("failed") : route.continue(),
  );
  await page.locator("#save-document").click();
  await page
    .locator(".editor-error")
    .getByText(/nicht gespeichert/)
    .waitFor();
  assert.equal(await selectedUploadFileName(page), "vertrag.pdf");
  await page.unroute(UPLOAD_REQUESTS);

  // So does a rejected name.
  await documentName(page).fill("   ");
  const invalid = waitForPostToCurrentPage(page);
  await page.locator("#save-document").click();
  assert.equal((await invalid).status(), 422);
  assert.equal(await selectedUploadFileName(page), "vertrag.pdf");
  assert.equal(await fileVersionNote(page).inputValue(), "Erster Stand");

  // And leaving the form, when the discard prompt is declined.
  await page.keyboard.press("Escape");
  await page.locator("[data-confirm-cancel]").click();
  assert.equal(await selectedUploadFileName(page), "vertrag.pdf");

  await documentName(page).fill("Vertragsunterlagen");
  await uploadButton(page).click();
  await page.locator("[data-document-id]").waitFor();
  assert.equal(new URL(page.url()).hash, "#documents");
  const documentId = await page.locator("[data-document-id]").getAttribute("data-document-id");
  const downloadPath = `${casePath}/documents/${documentId}/1`;

  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: "Herunterladen", exact: true }).click();
  const received = await download;
  assert.equal(received.suggestedFilename(), "vertrag.pdf");
  assert.deepEqual(await readFile(await received.path()), FIRST_VERSION_BYTES);
  assert.ok(!(await page.content()).includes("File_ContentId"));

  record(
    "Document upload and authenticated download preserve the original filename and exact bytes",
  );
  record(
    "Upload validation, network failure and cancelled discard retain the selected file and note",
  );
  return { documentId, downloadPath };
}

// ---------------------------------------------------------------------------------------------
// File history across offer versions and restarts
// ---------------------------------------------------------------------------------------------

async function checkFileHistorySurvivesRestart({
  page,
  origin,
  output,
  browserRequest,
  record,
  restart,
  offerPath,
  downloadPath,
}) {
  await page.getByRole("link", { name: "Neue Version erfassen", exact: true }).click();
  await page.locator("#offer-Name").fill("Dokumentenofferte ergänzt");
  await page.locator("#save-offer").click();
  await page.locator('#offer-workspace[data-revision="2"]').waitFor();

  await newFileVersionLink(page).click();
  await fileInput(page).setInputFiles(pdfFile("vertrag-neu.pdf", SECOND_VERSION_BYTES));
  await fileVersionNote(page).fill("Nachverhandelt");
  await uploadButton(page).click();
  await page.locator("[data-document-id]").waitFor();
  await page.locator("#documents summary").click();
  assert.equal(await page.locator("[data-document-version]").count(), 2);
  assert.match(await page.locator('[data-document-version="2"]').textContent(), /Version 2/);
  assert.equal(
    await downloadedText(browserRequest, page, downloadPath),
    FIRST_VERSION_BYTES.toString(),
  );
  await page
    .locator("#documents")
    .screenshot({ path: join(output, "18-document-history-desktop.png") });

  // The first offer version keeps pointing at the file it was saved with.
  await page.goto(`${origin}${offerPath}?revision=1`);
  assert.equal(await page.locator("[data-document-version]").count(), 1);
  assert.equal(await newFileVersionLink(page).count(), 0);
  assert.ok(!(await page.locator("#documents").textContent()).includes("vertrag-neu.pdf"));

  await restart();
  await page.goto(`${origin}${offerPath}`);
  assert.match(await page.locator("#documents").textContent(), /vertrag-neu.pdf/);
  assert.equal(
    await downloadedText(browserRequest, page, downloadPath),
    FIRST_VERSION_BYTES.toString(),
  );

  record(
    "File history retains earlier bytes and offer-version references across application restarts",
  );
}

// ---------------------------------------------------------------------------------------------
// Readers, editors, concurrent uploads and revoked access
// ---------------------------------------------------------------------------------------------

async function checkReaderMayOnlyDownload(context) {
  const { reader, origin, browserRequest, offerPath, downloadPath } = context;

  const version = await evaluationVersion(context);
  assert.equal((await shareEvaluation(context, "Reader", version)).status(), 200);

  await reader.page.goto(`${origin}${offerPath}`);
  assert.equal(
    await reader.page.getByRole("link", { name: "Dokument hinzufügen", exact: true }).count(),
    0,
  );
  assert.equal(
    await downloadedText(browserRequest, reader.page, downloadPath),
    FIRST_VERSION_BYTES.toString(),
  );
  assert.equal((await browserRequest(reader.page, `${offerPath}/documents/upload`)).status(), 403);
  assert.equal(
    await postUploadDirectly(reader.page, `${offerPath}/documents/upload`, {
      Version: String(Number(version) + 1),
      Name: "Denied",
    }),
    403,
  );
}

async function checkConcurrentUploadConflict(context) {
  const { page, reader, origin, browserRequest, record, casePath, offerPath, documentId } = context;

  const version = await evaluationVersion(context);
  assert.equal((await shareEvaluation(context, "Editor", version)).status(), 200);

  // The owner prepares an upload, then the editor saves a file version first.
  await page.goto(`${origin}${offerPath}/documents/upload/${documentId}`);
  const stale = await page.locator("[name=Version]").inputValue();
  await fileVersionNote(page).fill("Entwurf bleibt erhalten");
  await fileInput(page).setInputFiles(pdfFile("draft.pdf", FIRST_VERSION_BYTES));

  await reader.page.goto(`${origin}${offerPath}/documents/upload/${documentId}`);
  await fileInput(reader.page).setInputFiles(pdfFile("vertrag-final.pdf", SECOND_VERSION_BYTES));
  await uploadButton(reader.page).click();
  await reader.page.locator("[data-document-id]").waitFor();

  const conflict = waitForPostToCurrentPage(page);
  await uploadButton(page).click();
  assert.equal((await conflict).status(), 409);
  assert.equal(await fileVersionNote(page).inputValue(), "Entwurf bleibt erhalten");
  assert.equal(await selectedUploadFileName(page), "draft.pdf");
  assert.equal(await page.locator("[name=Version]").inputValue(), stale);
  assert.equal(
    await postUploadDirectly(
      page,
      `${offerPath}/documents/upload`,
      { Version: stale, Name: "No token" },
      false,
    ),
    400,
  );

  await page.getByRole("button", { name: "Stand geprüft – erneut speichern", exact: true }).click();
  await page.locator("[data-document-id]").waitFor();
  assert.equal(await page.locator("[data-document-version]").count(), 4);
  assert.match(
    await page.locator('[data-document-version="4"]').textContent(),
    /Entwurf bleibt erhalten/,
  );
  assert.equal(
    await downloadedText(browserRequest, page, `${casePath}/documents/${documentId}/4`),
    FIRST_VERSION_BYTES.toString(),
  );

  record(
    "An explicit upload conflict retry keeps the original file bytes and appends a separate file version",
  );
}

async function checkRevokedAccess(context) {
  const { page, reader, post, browserRequest, record, casePath, downloadPath } = context;

  const version = await evaluationVersion(context);
  assert.equal(
    (
      await post(page, casePath, {
        action: "revoke",
        userId: await page.locator(".share-row input[name=userId]").inputValue(),
        version,
      })
    ).status(),
    200,
  );
  assert.equal((await browserRequest(reader.page, downloadPath)).status(), 403);

  record(
    "Document sharing covers direct upload/download requests, editors, revocation, CSRF and concurrent changes",
  );
}

// ---------------------------------------------------------------------------------------------
// Phone layout
// ---------------------------------------------------------------------------------------------

async function checkPhoneLayout({ page, origin, output, offerPath }) {
  await page.goto(`${origin}${offerPath}`);
  await page.setViewportSize(PHONE_VIEWPORT);
  await page.locator("#documents summary").click();
  assert.ok(await fitsViewportWidth(page));
  await page.locator("#documents").screenshot({ path: join(output, "19-documents-mobile.png") });

  await newFileVersionLink(page).click();
  assert.ok(await fitsViewportWidth(page));
  await page.screenshot({ path: join(output, "20-document-upload-mobile.png"), fullPage: true });
  await page.setViewportSize(DESKTOP_VIEWPORT);
}

// ---------------------------------------------------------------------------------------------
// ZIP export, import as a new evaluation, and deletion of the original
// ---------------------------------------------------------------------------------------------

async function exportAndImportEvaluation({
  page,
  origin,
  output,
  browserRequest,
  casePath,
  documentId,
}) {
  await page.goto(`${origin}${casePath}`);
  const exported = page.waitForEvent("download");
  await page
    .locator(".workflow-nav")
    .getByRole("link", { name: "Grundlagen", exact: true })
    .click();
  await page
    .locator(".workspace-subnav")
    .getByRole("link", { name: "Allgemein", exact: true })
    .click();
  await page
    .locator("#evaluation-data")
    .getByRole("link", { name: "Exportieren", exact: true })
    .click();
  const zip = await exported;
  assert.match(zip.suggestedFilename(), /\.zip$/);
  const zipPath = join(output, "document-export.zip");
  await zip.saveAs(zipPath);

  await page.goto(origin);
  await page.getByRole("button", { name: "Evaluation importieren", exact: true }).click();
  await page.getByLabel("Exportdatei (JSON oder ZIP)").setInputFiles(zipPath);
  await page.getByRole("button", { name: "Als neue Evaluation importieren", exact: true }).click();
  await page.waitForURL(/\/evaluations\//);
  const importedPath = new URL(page.url()).pathname;
  assert.notEqual(importedPath, casePath);
  assert.equal(await page.locator(".share-row").count(), 0);

  // The import carries its own copies of the document and every file version.
  await page.goto(`${origin}${importedPath}/offers`);
  await page.locator(".offer-card a").first().click();
  await page.locator("[data-document-id]").waitFor();
  const copiedId = await page.locator("[data-document-id]").getAttribute("data-document-id");
  assert.notEqual(copiedId, documentId);
  assert.equal(await page.locator("[data-document-version]").count(), 4);
  const copiedDownloadPath = `${importedPath}/documents/${copiedId}/1`;
  assert.equal(
    await downloadedText(browserRequest, page, copiedDownloadPath),
    FIRST_VERSION_BYTES.toString(),
  );

  return { copiedDownloadPath };
}

async function checkDeletionLeavesImportedCopy({
  page,
  origin,
  browserRequest,
  record,
  offerPath,
  downloadPath,
  copiedDownloadPath,
}) {
  await page.goto(`${origin}${offerPath}`);

  // Cancelling the confirmation keeps the document and returns focus to the button.
  await deleteDocumentButton(page).click();
  assert.match(await page.locator("#confirmation-title").textContent(), /Vertragsunterlagen/);
  assert.equal(
    await page
      .locator("[data-confirm-cancel]")
      .evaluate((element) => element === document.activeElement),
    true,
  );
  await page.keyboard.press("Escape");
  assert.equal(await page.locator("[data-document-id]").count(), 1);
  assert.equal(
    await deleteDocumentButton(page).evaluate((element) => element === document.activeElement),
    true,
  );

  const deleted = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === offerPath && response.request().method() === "POST",
  );
  await deleteDocumentButton(page).click();
  await page.locator("[data-confirm-accept]").click();
  assert.equal((await deleted).status(), 200);
  await page
    .locator("#documents")
    .getByText("Zu diesem Offertstand sind noch keine Dokumente abgelegt.")
    .waitFor();
  await page.waitForFunction(() => document.activeElement?.id === "documents-title");
  assert.equal((await browserRequest(page, downloadPath)).status(), 404);
  assert.equal(
    await downloadedText(browserRequest, page, copiedDownloadPath),
    FIRST_VERSION_BYTES.toString(),
  );

  record(
    "Responsive document screens and ZIP import retain all file versions independently of deleted originals",
  );
}

// ---------------------------------------------------------------------------------------------
// Expired authentication while an upload is being prepared
// ---------------------------------------------------------------------------------------------

async function checkExpiredSessionKeepsUpload({ page, origin, record, offerPath }) {
  const expiredContext = await page
    .context()
    .browser()
    .newContext({
      storageState: await page.context().storageState(),
      ignoreHTTPSErrors: origin.startsWith("https:"),
    });
  const expiredPage = await expiredContext.newPage();
  expiredPage.setDefaultTimeout(EXPIRED_SESSION_TIMEOUT_MS);

  await expiredPage.goto(`${origin}${offerPath}/documents/upload`);
  await documentName(expiredPage).fill("Unsent upload");
  await expiredPage.locator("#upload").setInputFiles(pdfFile("unsent.pdf", FIRST_VERSION_BYTES));
  await expiredContext.clearCookies();
  await expiredPage.locator("#save-document").click();
  await expiredPage
    .locator(".editor-error")
    .getByText(/nicht mehr bearbeiten/)
    .waitFor();
  assert.equal(await selectedUploadFileName(expiredPage), "unsent.pdf");
  assert.equal(await documentName(expiredPage).inputValue(), "Unsent upload");
  assert.equal(await expiredPage.locator("#save-document").isDisabled(), true);
  await expiredContext.close();

  record("Expired authentication keeps the selected upload file visible and disables saving");
}
