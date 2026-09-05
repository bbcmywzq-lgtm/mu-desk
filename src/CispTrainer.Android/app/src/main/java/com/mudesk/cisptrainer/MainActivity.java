package com.mudesk.cisptrainer;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.webkit.JavascriptInterface;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONArray;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

public final class MainActivity extends Activity {
    private static final String SOURCE_URL = "https://github.com/npcola/CISP";
    private WebView webView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        webView = new WebView(this);
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setDefaultTextEncodingName("UTF-8");
        webView.addJavascriptInterface(new AndroidBridge(), "AndroidBridge");
        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                if ("file".equalsIgnoreCase(uri.getScheme())) {
                    return false;
                }
                openExternal(uri);
                return true;
            }
        });
        setContentView(webView);
        webView.loadUrl("file:///android_asset/index.html");
    }

    @Override
    public void onBackPressed() {
        if (webView.canGoBack()) {
            webView.goBack();
        } else {
            super.onBackPressed();
        }
    }

    private void openExternal(Uri uri) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, uri));
        } catch (Exception exception) {
            Toast.makeText(this, "没有可打开该链接的应用。", Toast.LENGTH_SHORT).show();
        }
    }

    private final class AndroidBridge {
        @JavascriptInterface
        public void openSource() {
            runOnUiThread(() -> openExternal(Uri.parse(SOURCE_URL)));
        }

        @JavascriptInterface
        public void copyText(String text) {
            runOnUiThread(() -> {
                ClipboardManager clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                clipboard.setPrimaryClip(ClipData.newPlainText("CISP 题目", text));
                Toast.makeText(MainActivity.this, "已复制。", Toast.LENGTH_SHORT).show();
            });
        }

        @JavascriptInterface
        public void exportWrongBook(String markdown, String imageNamesJson) {
            new Thread(() -> {
                try {
                    File exportDirectory = new File(getCacheDir(), "exports");
                    if (!exportDirectory.exists() && !exportDirectory.mkdirs()) {
                        throw new IllegalStateException("无法建立导出目录");
                    }
                    String timestamp = new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.CHINA).format(new Date());
                    File zipFile = new File(exportDirectory, "CISP错题-" + timestamp + ".zip");
                    Set<String> uniqueNames = new HashSet<>();
                    JSONArray names = new JSONArray(imageNamesJson);
                    try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(zipFile))) {
                        zip.putNextEntry(new ZipEntry("错题请教.md"));
                        zip.write(markdown.getBytes(StandardCharsets.UTF_8));
                        zip.closeEntry();
                        for (int index = 0; index < names.length(); index++) {
                            String name = names.optString(index, "");
                            if (!name.matches("[A-Za-z0-9._-]+") || !uniqueNames.add(name)) {
                                continue;
                            }
                            try (InputStream input = getAssets().open("images/" + name)) {
                                zip.putNextEntry(new ZipEntry("images/" + name));
                                copyStream(input, zip);
                                zip.closeEntry();
                            }
                        }
                    }
                    shareFile(zipFile, "application/zip", "把 CISP 错题发给 AI");
                } catch (Exception exception) {
                    showToast("导出失败：" + exception.getMessage());
                }
            }).start();
        }

        @JavascriptInterface
        public void openPdf(String assetName) {
            if (assetName == null || !assetName.matches("[A-Za-z0-9._-]+\\.pdf")) {
                showToast("材料名称无效。");
                return;
            }
            new Thread(() -> {
                try {
                    File materialDirectory = new File(getCacheDir(), "materials");
                    if (!materialDirectory.exists() && !materialDirectory.mkdirs()) {
                        throw new IllegalStateException("无法建立材料目录");
                    }
                    File output = new File(materialDirectory, assetName);
                    try (InputStream input = getAssets().open("pdfs/" + assetName);
                         FileOutputStream stream = new FileOutputStream(output)) {
                        copyStream(input, stream);
                    }
                    shareFile(output, "application/pdf", "打开 CISP 原版材料");
                } catch (Exception exception) {
                    showToast("材料打开失败：" + exception.getMessage());
                }
            }).start();
        }

        private void shareFile(File file, String mimeType, String chooserTitle) {
            Uri uri = Uri.parse("content://" + getPackageName() + ".files/" + Uri.encode(file.getName()));
            Intent intent = new Intent(Intent.ACTION_SEND);
            intent.setType(mimeType);
            intent.putExtra(Intent.EXTRA_STREAM, uri);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            runOnUiThread(() -> startActivity(Intent.createChooser(intent, chooserTitle)));
        }

        private void showToast(String message) {
            runOnUiThread(() -> Toast.makeText(MainActivity.this, message, Toast.LENGTH_LONG).show());
        }
    }

    private static void copyStream(InputStream input, OutputStream output) throws IOException {
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = input.read(buffer)) != -1) {
            output.write(buffer, 0, read);
        }
    }
}
