// SPDX-License-Identifier: AGPL-3.0-only
// Images in the designer (AWK-17 step 4b): one place that uploads and names image addresses, and one file button.
import { createContext, useContext, useRef } from "react";

export interface ImageTools {
  /** Stores an image and gives its id. */
  upload: (file: File) => Promise<string>;
  /** Where the designer shows a stored image; a width asks for a smaller copy. */
  url: (assetId: string, width?: number) => string;
}

export const Images = createContext<ImageTools>({ upload: () => Promise.reject(new Error("No image upload here.")), url: (assetId) => assetId });

export const useImages = () => useContext(Images);

/** A visible button that opens the file chooser; the hidden input carries the accessible file label. */
export function ImageButton({ label, fileLabel, onImage }: { label: string; fileLabel: string; onImage: (assetId: string) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const { upload } = useImages();

  return (
    <>
      <button
        type="button"
        className="designer-button designer-button--quiet"
        onClick={(event) => {
          event.stopPropagation();
          input.current?.click();
        }}
      >
        {label}
      </button>
      <input
        ref={input}
        type="file"
        className="designer-visually-hidden"
        aria-label={fileLabel}
        accept="image/png,image/jpeg,image/webp"
        tabIndex={-1}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = "";

          if (file !== undefined) {
            void upload(file).then(onImage);
          }
        }}
      />
    </>
  );
}
