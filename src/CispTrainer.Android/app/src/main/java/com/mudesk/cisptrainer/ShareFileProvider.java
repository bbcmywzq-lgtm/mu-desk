package com.mudesk.cisptrainer;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

public final class ShareFileProvider extends ContentProvider {
    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public String getType(Uri uri) {
        String name = uri.getLastPathSegment();
        return name != null && name.toLowerCase().endsWith(".pdf")
            ? "application/pdf"
            : "application/zip";
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File file = resolve(uri);
        MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        cursor.addRow(new Object[] { file.getName(), file.length() });
        return cursor;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) {
            throw new FileNotFoundException("Only read access is allowed");
        }
        File file = resolve(uri);
        if (!file.isFile()) {
            throw new FileNotFoundException(file.getAbsolutePath());
        }
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    private File resolve(Uri uri) {
        String name = uri.getLastPathSegment();
        if (name == null || !name.matches("[A-Za-z0-9._\\-\\u4e00-\\u9fa5]+")) {
            return new File(getContext().getCacheDir(), "missing");
        }
        File exports = new File(getContext().getCacheDir(), "exports/" + name);
        if (exports.isFile()) {
            return exports;
        }
        return new File(getContext().getCacheDir(), "materials/" + name);
    }

    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] selectionArgs) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { throw new UnsupportedOperationException(); }
}
