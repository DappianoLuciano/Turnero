import productJson from '@product';

/** Branding del producto. Fuente única: /product.json en la raíz del repo. */
export const product = productJson as {
  name: string;
  shortName: string;
  description: string;
  themeColor: string;
  backgroundColor: string;
  /** Where owners write to activate their subscription. Optional: set it in /product.json. */
  supportEmail?: string;
};
